// Hammer-MCP adapter: one DLL per bitness (hmcp-adapter-x86.dll / hmcp-adapter-x64.dll) for every registry build with
// an "injectAdapter" support level. It finds out itself which build it runs in (sha256 of this process's editor file)
// and reports it in hello. Layers 0-1 (pipe, MCP menu) touch no Hammer internals; build-specific knowledge for later
// layers comes as data (signatures / offsets per build), not as separate DLLs.
#include "hmcp.h"
#include "live.h"
#include <sddl.h>
#include <commctrl.h>
#include <cstdint>
#include <deque>
#include <mutex>
#include <string>
#include <vector>

namespace {

#if defined(_WIN64)
constexpr const char* kAdapterId = "x64";
#else
constexpr const char* kAdapterId = "x86";
#endif

// ---------- small JSON helpers (flat objects; enough for PROTOCOL.md) ----------

std::string JEsc(const std::string& s) {
    std::string o;
    for (unsigned char c : s) {
        switch (c) {
        case '"': o += "\\\""; break;
        case '\\': o += "\\\\"; break;
        case '\n': o += "\\n"; break;
        case '\r': o += "\\r"; break;
        case '\t': o += "\\t"; break;
        default:
            if (c < 0x20) { char b[8]; snprintf(b, sizeof b, "\\u%04x", c); o += b; }
            else o += (char)c;
        }
    }
    return o;
}

// parses a JSON string starting at s[i] == '"'; returns the unescaped value, i ends after the closing quote
std::string JParseString(const std::string& s, size_t& i) {
    std::string o;
    for (++i; i < s.size() && s[i] != '"'; ++i) {
        if (s[i] != '\\' || i + 1 >= s.size()) { o += s[i]; continue; }
        char e = s[++i];
        switch (e) {
        case 'n': o += '\n'; break;
        case 'r': o += '\r'; break;
        case 't': o += '\t'; break;
        case 'u':
            if (i + 4 < s.size()) {
                unsigned cp = (unsigned)strtoul(s.substr(i + 1, 4).c_str(), nullptr, 16);
                i += 4;
                if (cp < 0x80) o += (char)cp;
                else if (cp < 0x800) { o += (char)(0xC0 | (cp >> 6)); o += (char)(0x80 | (cp & 0x3F)); }
                else { o += (char)(0xE0 | (cp >> 12)); o += (char)(0x80 | ((cp >> 6) & 0x3F)); o += (char)(0x80 | (cp & 0x3F)); }
            }
            break;
        default: o += e;
        }
    }
    if (i < s.size()) ++i;
    return o;
}

// position right after "key": (and spaces), npos if absent
size_t JFind(const std::string& j, const char* key) {
    std::string k = std::string("\"") + key + "\"";
    size_t p = j.find(k);
    if (p == std::string::npos) return p;
    p = j.find(':', p + k.size());
    if (p == std::string::npos) return p;
    for (++p; p < j.size() && (j[p] == ' ' || j[p] == '\t'); ++p) {}
    return p;
}

std::string JStr(const std::string& j, const char* key, const std::string& def = "") {
    size_t p = JFind(j, key);
    if (p == std::string::npos || p >= j.size() || j[p] != '"') return def;
    return JParseString(j, p);
}

long JInt(const std::string& j, const char* key, long def = 0) {
    size_t p = JFind(j, key);
    return p == std::string::npos ? def : strtol(j.c_str() + p, nullptr, 10);
}

bool JBool(const std::string& j, const char* key, bool def = false) {
    size_t p = JFind(j, key);
    if (p == std::string::npos) return def;
    return j.compare(p, 4, "true") == 0;
}

// the text of an array value ("[ ... ]") of key, without the brackets
std::string JArray(const std::string& j, const char* key) {
    size_t p = JFind(j, key);
    if (p == std::string::npos || p >= j.size() || j[p] != '[') return {};
    int depth = 0; bool inStr = false;
    for (size_t i = p; i < j.size(); ++i) {
        char c = j[i];
        if (inStr) { if (c == '\\') ++i; else if (c == '"') inStr = false; continue; }
        if (c == '"') inStr = true;
        else if (c == '[') ++depth;
        else if (c == ']' && --depth == 0) return j.substr(p + 1, i - p - 1);
    }
    return {};
}

std::vector<std::string> JStrings(const std::string& arr) {
    std::vector<std::string> out;
    for (size_t i = 0; i < arr.size();) {
        if (arr[i] == '"') out.push_back(JParseString(arr, i));
        else ++i;
    }
    return out;
}

// flat objects of an array ("{...},{...}")
std::vector<std::string> JObjects(const std::string& arr) {
    std::vector<std::string> out; int depth = 0; size_t start = 0; bool inStr = false;
    for (size_t i = 0; i < arr.size(); ++i) {
        char c = arr[i];
        if (inStr) { if (c == '\\') ++i; else if (c == '"') inStr = false; continue; }
        if (c == '"') inStr = true;
        else if (c == '{' && depth++ == 0) start = i;
        else if (c == '}' && --depth == 0) out.push_back(arr.substr(start, i - start + 1));
    }
    return out;
}

std::wstring W(const std::string& utf8) {
    if (utf8.empty()) return {};
    int n = MultiByteToWideChar(CP_UTF8, 0, utf8.data(), (int)utf8.size(), nullptr, 0);
    std::wstring w(n, L'\0');
    MultiByteToWideChar(CP_UTF8, 0, utf8.data(), (int)utf8.size(), w.data(), n);
    return w;
}

std::string U8(const std::wstring& w) {
    if (w.empty()) return {};
    int n = WideCharToMultiByte(CP_UTF8, 0, w.data(), (int)w.size(), nullptr, 0, nullptr, nullptr);
    std::string s(n, '\0');
    WideCharToMultiByte(CP_UTF8, 0, w.data(), (int)w.size(), s.data(), n, nullptr, nullptr);
    return s;
}

// ---------- shared state ----------

struct ServerState {
    bool connected = false;
    std::string server = "?", mode = "?", agent = "active", approval = "auto", notify;
    long pending = 0;
    bool canUndo = false;
};

struct ChatLine { std::string from, text, at; };

constexpr size_t kLogMax = 500, kChatMax = 200;

HMODULE g_self = nullptr;
std::mutex g_lock;            // guards everything below up to g_profile
ServerState g_state;
std::deque<std::string> g_out;
std::deque<std::string> g_log;          // the agent's activity, newest kLogMax lines
std::vector<std::string> g_logNew;      // lines the log window has not shown yet
bool g_logReset = false;                // g_log was replaced as a whole: the window reloads it
std::deque<ChatLine> g_chat;            // newest kChatMax chat lines
std::vector<std::pair<std::string, std::string>> g_pendingItems;  // id, text
std::string g_build = "unknown", g_sha, g_support = "none";
std::string g_profile;                  // the server's {"cmd":"profile"} line; empty = the embedded data (allowed_builds.h)
HANDLE g_outEvent = nullptr;   // something queued in g_out
HANDLE g_stopEvent = nullptr;  // unload without a server (menu "Disconnect" while not connected)

// UI: g_hwnd is set by the worker before the subclass is installed; the rest is touched on the UI thread only.
// The main window is subclassed with SetWindowSubclass (comctl32), not GWLP_WNDPROC: whoever subclasses it after us
// (MFC, uxtheme, the editor) keeps a valid chain when we remove ours and unload - restoring an old GWLP_WNDPROC left
// their "previous proc" pointing into our unloaded code (crash on the next WM_SYSCOMMAND).
HWND g_hwnd = nullptr;
constexpr UINT_PTR kSubclassId = 0x484D4350;   // 'HMCP'
bool g_subclassed = false;                     // UI thread
WNDPROC g_origProc = nullptr;                  // the window proc BootProc forwards to
volatile bool g_bootInChain = false;           // BootProc is still somebody's window proc: never unload the DLL
volatile bool g_bootDone = false;
HMENU g_menu = nullptr;
volatile LONG g_dialogs = 0;   // our modal dialogs open right now

constexpr UINT WM_HMCP_INSTALL = WM_APP + 0x1E0;
constexpr UINT WM_HMCP_UPDATE = WM_APP + 0x1E1;
constexpr UINT WM_HMCP_LOG = WM_APP + 0x1E2;
constexpr UINT WM_HMCP_UNINSTALL = WM_APP + 0x1E3;
constexpr UINT WM_HMCP_PENDING = WM_APP + 0x1E4;
constexpr UINT WM_HMCP_CHAT = WM_APP + 0x1E5;   // lParam 1 = a line from the agent arrived
constexpr UINT WM_HMCP_LIVE = WM_APP + 0x1E6;   // layer 2 read on the UI thread; lParam = LiveRequest*

struct LiveRequest { bool camera; std::string id, answer; };
constexpr UINT_PTR kTimerId = 0xBE5F;

// command ids far above Hammer's own (its highest are around 0x8100)
enum : UINT {
    ID_PAUSE = 0xBE00, ID_SELECTION, ID_CHAT, ID_PENDING, ID_UNDO, ID_LOG, ID_STATUS, ID_DISCONNECT, ID_ABOUT,
};

void Send(const std::string& line) {
    { std::lock_guard l(g_lock); g_out.push_back(line); }
    SetEvent(g_outEvent);
}

ServerState State() { std::lock_guard l(g_lock); return g_state; }

void PostUi(UINT msg, LPARAM lp = 0) { if (g_hwnd) PostMessageW(g_hwnd, msg, 0, lp); }

std::wstring Crlf(const std::string& utf8) {
    std::wstring w = W(utf8), o;
    for (wchar_t c : w) {
        if (c == L'\r') continue;
        if (c == L'\n') o += L'\r';
        o += c;
    }
    return o;
}

// ---------- message boxes (status, about) ----------

int Box(const std::wstring& text, UINT flags) {
    InterlockedIncrement(&g_dialogs);
    MSGBOXPARAMSW p{ sizeof p };
    p.hwndOwner = g_hwnd;
    p.hInstance = g_self;
    p.lpszText = text.c_str();
    p.lpszCaption = L"Hammer-MCP";
    p.dwStyle = flags | MB_USERICON;
    p.lpszIcon = MAKEINTRESOURCEW(1);
    int r = MessageBoxIndirectW(&p);
    InterlockedDecrement(&g_dialogs);
    return r;
}

std::wstring StatusText() {
    auto s = State();
    std::string sha, build, support, profile;
    { std::lock_guard l(g_lock); sha = g_sha; build = g_build; support = g_support; profile = g_profile; }
    std::wstring t = L"Adapter: " + W(kAdapterId) + L" " + W(HMCP_NATIVE_VERSION) + L" (support: " + W(support) +
                     (profile.empty() ? L", built-in profile" : L", profile from the server") + L")\n";
    t += L"Hammer build: " + W(build) + L" (" + W(sha.substr(0, 12)) + L"…)\n\n";
    if (!s.connected) return t + L"Hammer-MCP server: not connected";
    t += L"Hammer-MCP server: " + W(s.server) + L", mode " + W(s.mode) + L"\n";
    t += L"Agent: " + W(s.agent) + L", edits: " + W(s.approval == "ask" ? "ask me" : "automatic");
    if (s.pending) t += L", " + std::to_wstring(s.pending) + L" pending";
    if (!s.notify.empty()) t += L"\n\nLast message: " + W(s.notify);
    return t;
}

void ShowAbout() {
    Box(L"Hammer-MCP adapter\n\n" + StatusText() +
        L"\n\nLets AI agents see and edit the map open in Hammer.\nhttps://github.com/EpicMorg/Hammer-MCP\n"
        L"https://developer.valvesoftware.com/wiki/Hammer_MCP_Server\n\n(c) 2026 EpicMorg, MIT License",
        MB_OK);
}

// ---------- menu icons: glyphs of the system icon font; About = our logo ----------

std::vector<HBITMAP> g_menuBitmaps;

int CALLBACK FontEnumProc(const LOGFONTW* f, const TEXTMETRICW*, DWORD, LPARAM p) {
    auto found = reinterpret_cast<int*>(p);
    if (!wcscmp(f->lfFaceName, L"Segoe Fluent Icons")) *found |= 1;
    if (!wcscmp(f->lfFaceName, L"Segoe MDL2 Assets")) *found |= 2;
    return 1;
}

// Windows 11: Segoe Fluent Icons, Windows 10: Segoe MDL2 Assets (same code points), older: none (text-only items)
const wchar_t* IconFont() {
    int found = 0;
    HDC dc = GetDC(nullptr);
    LOGFONTW lf{};
    lf.lfCharSet = DEFAULT_CHARSET;
    EnumFontFamiliesExW(dc, &lf, FontEnumProc, reinterpret_cast<LPARAM>(&found), 0);
    ReleaseDC(nullptr, dc);
    return (found & 1) ? L"Segoe Fluent Icons" : (found & 2) ? L"Segoe MDL2 Assets" : nullptr;
}

HBITMAP NewDib(int size, uint32_t** px) {
    BITMAPINFO bi{};
    bi.bmiHeader = { sizeof(BITMAPINFOHEADER), size, -size, 1, 32, BI_RGB };
    HBITMAP b = CreateDIBSection(nullptr, &bi, DIB_RGB_COLORS, reinterpret_cast<void**>(px), nullptr, 0);
    if (b) memset(*px, 0, size * size * 4);
    return b;
}

// a premultiplied 32-bit bitmap of one glyph in the menu text colour (menus draw such bitmaps with alpha)
HBITMAP GlyphBitmap(const wchar_t* font, wchar_t glyph, int size) {
    uint32_t* px = nullptr;
    HBITMAP bmp = NewDib(size, &px);
    if (!bmp) return nullptr;
    HDC dc = CreateCompatibleDC(nullptr);
    HFONT f = CreateFontW(-size, 0, 0, 0, FW_NORMAL, 0, 0, 0, DEFAULT_CHARSET, 0, 0, ANTIALIASED_QUALITY, 0, font);
    HGDIOBJ oldBmp = SelectObject(dc, bmp), oldFont = SelectObject(dc, f);
    SetBkMode(dc, TRANSPARENT);
    SetTextColor(dc, RGB(255, 255, 255));
    RECT r{ 0, 0, size, size };
    DrawTextW(dc, &glyph, 1, &r, DT_CENTER | DT_VCENTER | DT_SINGLELINE | DT_NOPREFIX);
    GdiFlush();
    COLORREF c = GetSysColor(COLOR_MENUTEXT);
    for (int i = 0; i < size * size; ++i) {
        uint32_t a = px[i] & 0xFF;   // grey anti-aliasing: coverage in every channel
        px[i] = (a << 24) | ((GetRValue(c) * a / 255) << 16) | ((GetGValue(c) * a / 255) << 8) | (GetBValue(c) * a / 255);
    }
    SelectObject(dc, oldFont);
    SelectObject(dc, oldBmp);
    DeleteObject(f);
    DeleteDC(dc);
    return bmp;
}

HBITMAP LogoBitmap(int size) {
    HICON icon = static_cast<HICON>(LoadImageW(g_self, MAKEINTRESOURCEW(1), IMAGE_ICON, size, size, 0));
    if (!icon) return nullptr;
    uint32_t* px = nullptr;
    HBITMAP bmp = NewDib(size, &px);
    if (bmp) {
        HDC dc = CreateCompatibleDC(nullptr);
        HGDIOBJ old = SelectObject(dc, bmp);
        DrawIconEx(dc, 0, 0, icon, size, size, 0, nullptr, DI_NORMAL);
        SelectObject(dc, old);
        DeleteDC(dc);
    }
    DestroyIcon(icon);
    return bmp;
}

void SetMenuIcons() {
    int size = GetSystemMetrics(SM_CXSMICON);
    auto set = [](UINT id, HBITMAP b) {
        if (!b) return;
        g_menuBitmaps.push_back(b);
        MENUITEMINFOW mi{ sizeof mi };
        mi.fMask = MIIM_BITMAP;
        mi.hbmpItem = b;
        SetMenuItemInfoW(g_menu, id, FALSE, &mi);
    };
    if (const wchar_t* font = IconFont()) {
        const std::pair<UINT, wchar_t> glyphs[] = {
            { ID_PAUSE, 0xE769 },      // Pause
            { ID_SELECTION, 0xE8B3 },  // SelectAll
            { ID_CHAT, 0xE8BD },       // Message
            { ID_PENDING, 0xE9D5 },    // Checklist
            { ID_UNDO, 0xE7A7 },       // Undo
            { ID_LOG, 0xE81C },        // History
            { ID_STATUS, 0xE946 },     // Info
            { ID_DISCONNECT, 0xE8CD }, // DisconnectDrive
        };
        for (auto& [id, g] : glyphs) set(id, GlyphBitmap(font, g, size));
    }
    set(ID_ABOUT, LogoBitmap(size));
}

// ---------- tool windows (chat, activity log, pending changes) ----------
// Plain top-level windows owned by Hammer, opened from the MCP menu (docking into Hammer's panels needs the MFC work).
// Keys for them are taken out of Hammer's message loop by a WH_GETMESSAGE hook (MsgHook), so Hammer's accelerators
// (Delete, Ctrl+Z, tool letters...) never fire while the user types in our windows.

enum Kind { kChat, kLog, kPending, kKinds };
enum : int { CTL_ICON = 10, CTL_TITLE, CTL_TEXT, CTL_INPUT, CTL_LIST, CTL_APPLY_SEL = 20, CTL_APPLY_ALL, CTL_REJECT_ALL };

constexpr wchar_t kToolClass[] = L"HammerMcpToolWindow";
HWND g_wnd[kKinds]{};
HFONT g_font = nullptr, g_fontTitle = nullptr;
int g_fontH = 16;
HICON g_iconBig = nullptr, g_iconSmall = nullptr;
HHOOK g_msgHook = nullptr;
bool g_classOk = false;
bool g_pendingWanted = false;   // the user asked for the pending list: open the window when it arrives
long g_pendingShownCount = -1;  // server's pending count when the list window was last filled
std::vector<std::pair<std::string, std::string>> g_pendingShown;   // rows of the list window (id, text)

int Scale(int px) { return px * g_fontH / 16; }

int KindOf(HWND root) {
    for (int k = 0; k < kKinds; ++k)
        if (root && g_wnd[k] == root) return k;
    return -1;
}

HWND Child(HWND parent, const wchar_t* cls, const wchar_t* text, DWORD style, int id, DWORD ex = 0) {
    HWND h = CreateWindowExW(ex, cls, text, style | WS_CHILD | WS_VISIBLE, 0, 0, 0, 0, parent,
                             reinterpret_cast<HMENU>(static_cast<INT_PTR>(id)), g_self, nullptr);
    SendMessageW(h, WM_SETFONT, reinterpret_cast<WPARAM>(id == CTL_TITLE ? g_fontTitle : g_font), FALSE);
    return h;
}

void CreateChildren(HWND w, int kind) {
    HWND icon = Child(w, L"STATIC", nullptr, SS_ICON | SS_REALSIZEIMAGE, CTL_ICON);
    SendMessageW(icon, STM_SETICON, reinterpret_cast<WPARAM>(g_iconBig), 0);
    static const wchar_t* titles[] = { L"Chat with the agent", L"What the agent did", L"The agent wants to make these changes" };
    Child(w, L"STATIC", titles[kind], SS_LEFT | SS_CENTERIMAGE | SS_NOPREFIX | SS_ENDELLIPSIS, CTL_TITLE);
    DWORD ro = ES_MULTILINE | ES_READONLY | ES_AUTOVSCROLL | WS_VSCROLL | WS_TABSTOP;
    if (kind == kChat) {
        SendMessageW(Child(w, L"EDIT", L"", ro, CTL_TEXT, WS_EX_CLIENTEDGE), EM_SETLIMITTEXT, 0, 0);
        Child(w, L"EDIT", L"", ES_MULTILINE | ES_AUTOVSCROLL | ES_WANTRETURN | WS_VSCROLL | WS_TABSTOP, CTL_INPUT, WS_EX_CLIENTEDGE);
        Child(w, L"BUTTON", L"Send", BS_DEFPUSHBUTTON | WS_TABSTOP, IDOK);
    } else if (kind == kLog) {
        SendMessageW(Child(w, L"EDIT", L"", ro | WS_HSCROLL | ES_AUTOHSCROLL, CTL_TEXT, WS_EX_CLIENTEDGE), EM_SETLIMITTEXT, 0, 0);
    } else {
        HWND lv = Child(w, WC_LISTVIEWW, L"", LVS_REPORT | LVS_NOCOLUMNHEADER | LVS_SHOWSELALWAYS | LVS_SINGLESEL | WS_TABSTOP,
                        CTL_LIST, WS_EX_CLIENTEDGE);
        ListView_SetExtendedListViewStyle(lv, LVS_EX_CHECKBOXES | LVS_EX_FULLROWSELECT);
        LVCOLUMNW col{ LVCF_WIDTH };
        col.cx = 400;
        ListView_InsertColumn(lv, 0, &col);
        Child(w, L"BUTTON", L"Apply selected", BS_DEFPUSHBUTTON | WS_TABSTOP, CTL_APPLY_SEL);
        Child(w, L"BUTTON", L"Apply all", BS_PUSHBUTTON | WS_TABSTOP, CTL_APPLY_ALL);
        Child(w, L"BUTTON", L"Reject all", BS_PUSHBUTTON | WS_TABSTOP, CTL_REJECT_ALL);
        Child(w, L"BUTTON", L"Later", BS_PUSHBUTTON | WS_TABSTOP, IDCANCEL);
    }
}

void Layout(HWND w, int kind, int cw, int ch) {
    int m = Scale(8), iconS = GetSystemMetrics(SM_CXICON), top = m + iconS + m;
    int bh = g_fontH + Scale(12), bw = Scale(96);
    auto move = [&](int id, int x, int y, int cx, int cy) {
        if (HWND h = GetDlgItem(w, id)) MoveWindow(h, x, y, cx > 0 ? cx : 0, cy > 0 ? cy : 0, TRUE);
    };
    move(CTL_ICON, m, m, iconS, iconS);
    move(CTL_TITLE, m + iconS + m, m, cw - iconS - 3 * m, iconS);
    if (kind == kChat) {
        int ih = g_fontH * 3 + Scale(8);
        move(CTL_TEXT, m, top, cw - 2 * m, ch - top - ih - 2 * m);
        move(CTL_INPUT, m, ch - m - ih, cw - 3 * m - bw, ih);
        move(IDOK, cw - m - bw, ch - m - ih, bw, bh);
    } else if (kind == kLog) {
        move(CTL_TEXT, m, top, cw - 2 * m, ch - top - m);
    } else {
        move(CTL_LIST, m, top, cw - 2 * m, ch - top - bh - 2 * m);
        const int ids[] = { IDCANCEL, CTL_REJECT_ALL, CTL_APPLY_ALL, CTL_APPLY_SEL };   // right to left
        int x = cw - m;
        for (int id : ids) { x -= bw; move(id, x, ch - m - bh, bw, bh); x -= Scale(6); }
        if (HWND lv = GetDlgItem(w, CTL_LIST)) {
            RECT r;
            GetClientRect(lv, &r);
            ListView_SetColumnWidth(lv, 0, r.right);
        }
    }
}

void ScrollToEnd(HWND edit) {
    int n = GetWindowTextLengthW(edit);
    SendMessageW(edit, EM_SETSEL, n, n);
    SendMessageW(edit, EM_SCROLLCARET, 0, 0);
}

void FillChat() {
    HWND w = g_wnd[kChat];
    if (!w) return;
    std::deque<ChatLine> chat;
    { std::lock_guard l(g_lock); chat = g_chat; }
    bool on = State().connected;
    // always on top: this chat is a side channel - MCP has no way to wake an idle agent
    std::wstring t = L"Side chat: the agent reads it between its steps and answers here. If the agent is idle "
                     L"(waiting for you in its own window - Claude Code, Codex, Cursor...), write to it there, "
                     L"e.g. \"check the Hammer chat\".\r\n\r\n";
    for (auto& c : chat)
        t += L"[" + W(c.at) + L"] " + (c.from == "agent" ? L"Agent: " : L"You: ") + Crlf(c.text) + L"\r\n";
    if (chat.empty() && !on)
        t += L"Not connected to the Hammer-MCP server.\r\n";
    HWND e = GetDlgItem(w, CTL_TEXT);
    SetWindowTextW(e, t.c_str());
    ScrollToEnd(e);
    EnableWindow(GetDlgItem(w, CTL_INPUT), on);
    EnableWindow(GetDlgItem(w, IDOK), on);
}

void FillLog(bool reload) {
    std::vector<std::string> add;
    std::deque<std::string> all;
    {
        std::lock_guard l(g_lock);
        reload = reload || g_logReset;
        g_logReset = false;
        if (reload) { all = g_log; g_logNew.clear(); }
        else add.swap(g_logNew);
    }
    HWND w = g_wnd[kLog];
    if (!w) return;
    HWND e = GetDlgItem(w, CTL_TEXT);
    if (reload) {
        std::wstring t;
        for (auto& line : all) t += Crlf(line) + L"\r\n";
        if (t.empty()) t = State().connected ? L"The agent has not done anything yet.\r\n" : L"Not connected to the Hammer-MCP server.\r\n";
        SetWindowTextW(e, t.c_str());
        ScrollToEnd(e);
        return;
    }
    for (auto& line : add) {
        std::wstring t = Crlf(line) + L"\r\n";
        int n = GetWindowTextLengthW(e);
        SendMessageW(e, EM_SETSEL, n, n);
        SendMessageW(e, EM_REPLACESEL, FALSE, reinterpret_cast<LPARAM>(t.c_str()));
    }
    if (!add.empty() && SendMessageW(e, EM_GETLINECOUNT, 0, 0) > (LRESULT)(kLogMax + 100)) FillLog(true);
}

void FillPending() {
    HWND w = g_wnd[kPending];
    if (!w) return;
    { std::lock_guard l(g_lock); g_pendingShown = g_pendingItems; }
    g_pendingShownCount = State().pending;
    HWND lv = GetDlgItem(w, CTL_LIST);
    ListView_DeleteAllItems(lv);
    for (size_t i = 0; i < g_pendingShown.size(); ++i) {
        std::wstring text = W(g_pendingShown[i].second);
        LVITEMW it{ LVIF_TEXT };
        it.iItem = (int)i;
        it.pszText = text.data();
        ListView_InsertItem(lv, &it);
        ListView_SetCheckState(lv, (int)i, TRUE);
    }
    bool any = !g_pendingShown.empty();
    SetDlgItemTextW(w, CTL_TITLE, any ? L"The agent wants to make these changes" : L"No pending changes");
    for (int id : { CTL_APPLY_SEL, CTL_APPLY_ALL, CTL_REJECT_ALL }) EnableWindow(GetDlgItem(w, id), any && State().connected);
}

void SendChat(HWND w) {
    HWND in = GetDlgItem(w, CTL_INPUT);
    std::wstring text(GetWindowTextLengthW(in) + 1, L'\0');
    text.resize(GetWindowTextW(in, text.data(), (int)text.size()));
    while (!text.empty() && iswspace(text.back())) text.pop_back();
    if (text.empty() || !State().connected) return;
    Send("{\"type\":\"chat\",\"text\":\"" + JEsc(U8(text)) + "\"}");   // the server echoes it back as a chat line
    SetWindowTextW(in, L"");
}

void Decide(HWND w, int how) {   // CTL_APPLY_SEL / CTL_APPLY_ALL / CTL_REJECT_ALL
    HWND lv = GetDlgItem(w, CTL_LIST);
    std::string apply, reject;
    for (size_t i = 0; i < g_pendingShown.size(); ++i) {
        std::string id = "\"" + JEsc(g_pendingShown[i].first) + "\"";
        if (how == CTL_REJECT_ALL) reject += (reject.empty() ? "" : ",") + id;
        else if (how == CTL_APPLY_ALL || ListView_GetCheckState(lv, (int)i)) apply += (apply.empty() ? "" : ",") + id;
    }
    if (apply.empty() && reject.empty()) return;
    Send("{\"type\":\"approval\",\"apply\":[" + apply + "],\"reject\":[" + reject + "]}");
    DestroyWindow(w);   // unchecked changes stay pending; the menu shows the new count
}

LRESULT CALLBACK ToolProc(HWND w, UINT msg, WPARAM wp, LPARAM lp) {
    int kind = (int)GetWindowLongPtrW(w, GWLP_USERDATA);
    switch (msg) {
    case WM_CREATE:
        kind = (int)reinterpret_cast<INT_PTR>(reinterpret_cast<CREATESTRUCTW*>(lp)->lpCreateParams);
        SetWindowLongPtrW(w, GWLP_USERDATA, kind);
        CreateChildren(w, kind);
        return 0;
    case WM_SIZE:
        Layout(w, kind, LOWORD(lp), HIWORD(lp));
        return 0;
    case WM_CTLCOLORSTATIC:   // read-only edits (chat history, log) on a white page instead of the dialog grey
        if (GetDlgCtrlID(reinterpret_cast<HWND>(lp)) == CTL_TEXT) {
            SetBkColor(reinterpret_cast<HDC>(wp), GetSysColor(COLOR_WINDOW));
            SetTextColor(reinterpret_cast<HDC>(wp), GetSysColor(COLOR_WINDOWTEXT));
            return reinterpret_cast<LRESULT>(GetSysColorBrush(COLOR_WINDOW));
        }
        break;
    case WM_GETMINMAXINFO:
        reinterpret_cast<MINMAXINFO*>(lp)->ptMinTrackSize = { Scale(420), Scale(260) };
        return 0;
    case WM_COMMAND:
        switch (LOWORD(wp)) {
        case IDOK: if (kind == kChat) SendChat(w); else if (kind == kPending) Decide(w, CTL_APPLY_SEL); return 0;
        case CTL_APPLY_SEL: case CTL_APPLY_ALL: case CTL_REJECT_ALL: Decide(w, LOWORD(wp)); return 0;
        case IDCANCEL: DestroyWindow(w); return 0;
        }
        break;
    case WM_CLOSE:
        DestroyWindow(w);
        return 0;
    case WM_DESTROY:
        if (kind >= 0 && kind < kKinds && g_wnd[kind] == w) g_wnd[kind] = nullptr;
        return 0;
    }
    return DefWindowProcW(w, msg, wp, lp);
}

// opens (or brings up) a tool window; the callers fill it
HWND OpenTool(int kind, bool activate = true) {
    if (HWND w = g_wnd[kind]) {
        if (activate) {
            if (IsIconic(w)) ShowWindow(w, SW_RESTORE);
            SetForegroundWindow(w);
        }
        return w;
    }
    if (!g_classOk) return nullptr;
    static const wchar_t* captions[] = { L"Hammer-MCP: chat", L"Hammer-MCP: activity log", L"Hammer-MCP: pending changes" };
    static const SIZE sizes[] = { { 520, 480 }, { 680, 440 }, { 600, 380 } };
    int cx = Scale(sizes[kind].cx), cy = Scale(sizes[kind].cy);
    RECT r{};
    GetWindowRect(g_hwnd, &r);
    int x = r.left + (r.right - r.left - cx) / 2 + kind * Scale(24), y = r.top + (r.bottom - r.top - cy) / 2 + kind * Scale(24);
    HWND w = CreateWindowExW(WS_EX_CONTROLPARENT, kToolClass, captions[kind], WS_OVERLAPPEDWINDOW | WS_CLIPCHILDREN,
                             x, y, cx, cy, g_hwnd, nullptr, g_self, reinterpret_cast<LPVOID>(static_cast<INT_PTR>(kind)));
    if (!w) return nullptr;
    g_wnd[kind] = w;
    SendMessageW(w, WM_SETICON, ICON_BIG, reinterpret_cast<LPARAM>(g_iconBig));
    SendMessageW(w, WM_SETICON, ICON_SMALL, reinterpret_cast<LPARAM>(g_iconSmall));
    ShowWindow(w, activate ? SW_SHOW : SW_SHOWNOACTIVATE);
    if (activate && kind == kChat) SetFocus(GetDlgItem(w, CTL_INPUT));
    return w;
}

// Hammer's message loop would run its accelerators on keys meant for our windows: handle those keys here and hand
// Hammer a WM_NULL instead
LRESULT CALLBACK MsgHook(int code, WPARAM wp, LPARAM lp) {
    auto m = reinterpret_cast<MSG*>(lp);
    if (code == HC_ACTION && wp == PM_REMOVE && m->hwnd && m->message >= WM_KEYFIRST && m->message <= WM_KEYLAST) {
        HWND root = GetAncestor(m->hwnd, GA_ROOT);
        int kind = KindOf(root);
        if (kind >= 0) {
            bool done = false;
            if (m->message == WM_KEYDOWN) {
                int id = GetDlgCtrlID(m->hwnd);
                if (m->wParam == VK_RETURN && kind == kChat && id == CTL_INPUT && GetKeyState(VK_SHIFT) >= 0) {
                    SendChat(root);   // Enter sends, Shift+Enter is a new line
                    done = true;
                } else if (m->wParam == 'A' && GetKeyState(VK_CONTROL) < 0 && (id == CTL_INPUT || id == CTL_TEXT)) {
                    SendMessageW(m->hwnd, EM_SETSEL, 0, -1);
                    done = true;
                }
            }
            if (!done && !IsDialogMessageW(root, m)) {
                TranslateMessage(m);
                DispatchMessageW(m);
            }
            m->message = WM_NULL;
        }
    }
    return CallNextHookEx(g_msgHook, code, wp, lp);
}

void InstallUi() {
    INITCOMMONCONTROLSEX icc{ sizeof icc, ICC_LISTVIEW_CLASSES };
    InitCommonControlsEx(&icc);
    NONCLIENTMETRICSW ncm{ sizeof ncm };
    SystemParametersInfoW(SPI_GETNONCLIENTMETRICS, sizeof ncm, &ncm, 0);
    g_font = CreateFontIndirectW(&ncm.lfMessageFont);
    LOGFONTW title = ncm.lfMessageFont;
    title.lfHeight = title.lfHeight * 4 / 3;
    title.lfWeight = FW_SEMIBOLD;
    g_fontTitle = CreateFontIndirectW(&title);
    if (HDC dc = GetDC(nullptr)) {
        HGDIOBJ old = SelectObject(dc, g_font);
        TEXTMETRICW tm{};
        if (GetTextMetricsW(dc, &tm) && tm.tmHeight > 0) g_fontH = tm.tmHeight;
        SelectObject(dc, old);
        ReleaseDC(nullptr, dc);
    }
    g_iconBig = static_cast<HICON>(LoadImageW(g_self, MAKEINTRESOURCEW(1), IMAGE_ICON,
                                              GetSystemMetrics(SM_CXICON), GetSystemMetrics(SM_CYICON), 0));
    g_iconSmall = static_cast<HICON>(LoadImageW(g_self, MAKEINTRESOURCEW(1), IMAGE_ICON,
                                                GetSystemMetrics(SM_CXSMICON), GetSystemMetrics(SM_CYSMICON), 0));
    WNDCLASSEXW wc{ sizeof wc };
    wc.lpfnWndProc = ToolProc;
    wc.hInstance = g_self;
    wc.hIcon = g_iconBig;
    wc.hIconSm = g_iconSmall;
    wc.hCursor = LoadCursorW(nullptr, IDC_ARROW);
    wc.hbrBackground = reinterpret_cast<HBRUSH>(COLOR_BTNFACE + 1);
    wc.lpszClassName = kToolClass;
    g_classOk = RegisterClassExW(&wc) != 0 || GetLastError() == ERROR_CLASS_ALREADY_EXISTS;
    g_msgHook = SetWindowsHookExW(WH_GETMESSAGE, MsgHook, nullptr, GetCurrentThreadId());
}

void UninstallUi() {
    for (auto& w : g_wnd)
        if (w) DestroyWindow(w);   // WM_DESTROY clears the slot
    if (g_msgHook) { UnhookWindowsHookEx(g_msgHook); g_msgHook = nullptr; }
    if (g_classOk) UnregisterClassW(kToolClass, g_self);
    for (HBITMAP b : g_menuBitmaps) DeleteObject(b);
    g_menuBitmaps.clear();
    if (g_font) DeleteObject(g_font);
    if (g_fontTitle) DeleteObject(g_fontTitle);
    if (g_iconBig) DestroyIcon(g_iconBig);
    if (g_iconSmall) DestroyIcon(g_iconSmall);
}

// ---------- the MCP menu ----------

bool MenuPresent() {
    HMENU bar = GetMenu(g_hwnd);
    if (!bar || !g_menu) return false;
    for (int i = 0, n = GetMenuItemCount(bar); i < n; ++i)
        if (GetSubMenu(bar, i) == g_menu) return true;
    return false;
}

void BuildMenu() {
    if (!g_menu) {
        g_menu = CreatePopupMenu();
        AppendMenuW(g_menu, MF_STRING, ID_PAUSE, L"Agent paused");
        AppendMenuW(g_menu, MF_SEPARATOR, 0, nullptr);
        AppendMenuW(g_menu, MF_STRING | MF_GRAYED, ID_SELECTION, L"Send selection to agent");
        AppendMenuW(g_menu, MF_STRING, ID_CHAT, L"Chat with the agent...");
        AppendMenuW(g_menu, MF_STRING, ID_PENDING, L"Pending changes...");
        AppendMenuW(g_menu, MF_STRING, ID_UNDO, L"Undo agent's last step");
        AppendMenuW(g_menu, MF_SEPARATOR, 0, nullptr);
        AppendMenuW(g_menu, MF_STRING, ID_LOG, L"Activity log...");
        AppendMenuW(g_menu, MF_STRING, ID_STATUS, L"Status...");
        AppendMenuW(g_menu, MF_STRING, ID_DISCONNECT, L"Disconnect adapter");
        AppendMenuW(g_menu, MF_SEPARATOR, 0, nullptr);
        AppendMenuW(g_menu, MF_STRING, ID_ABOUT, L"About Hammer-MCP adapter...");
        SetMenuIcons();
    }
    HMENU bar = GetMenu(g_hwnd);
    if (bar && !MenuPresent()) {
        AppendMenuW(bar, MF_POPUP | MF_STRING, reinterpret_cast<UINT_PTR>(g_menu), L"&MCP");
        DrawMenuBar(g_hwnd);
    }
}

// after MFC's own WM_INITMENUPOPUP (which greys items it has no handler for) set ours from the server state
void UpdateMenu() {
    auto s = State();
    bool on = s.connected;
    auto enable = [](UINT id, bool yes) { EnableMenuItem(g_menu, id, MF_BYCOMMAND | (yes ? MF_ENABLED : MF_GRAYED)); };
    CheckMenuItem(g_menu, ID_PAUSE, MF_BYCOMMAND | (s.agent == "paused" ? MF_CHECKED : MF_UNCHECKED));
    std::wstring pending = L"Pending changes (" + std::to_wstring(s.pending) + L")...";
    MENUITEMINFOW mi{ sizeof mi };   // MIIM_STRING only: ModifyMenu would drop the item's icon
    mi.fMask = MIIM_STRING;
    mi.dwTypeData = pending.data();
    SetMenuItemInfoW(g_menu, ID_PENDING, FALSE, &mi);
    enable(ID_PAUSE, on);
    enable(ID_SELECTION, on && live::Ready());   // layer 2: the build has a memory layout
    enable(ID_CHAT, true);         // the window says when there is no server
    enable(ID_PENDING, on && s.approval == "ask" && s.pending > 0);
    enable(ID_UNDO, on && s.canUndo);
    enable(ID_LOG, true);
    enable(ID_STATUS, true);
    enable(ID_DISCONNECT, true);
    enable(ID_ABOUT, true);
}

// server state changed: menu (when built) and the open windows
void OnStateChanged() {
    if (g_menu) UpdateMenu();
    auto s = State();
    static bool wasOn = false;
    bool reconnected = s.connected && !wasOn;
    wasOn = s.connected;
    // windows stay open while the server is away (app restarted / killed) and catch up when it is back
    if (HWND w = g_wnd[kChat]) {
        EnableWindow(GetDlgItem(w, CTL_INPUT), s.connected);
        EnableWindow(GetDlgItem(w, IDOK), s.connected);
        SetDlgItemTextW(w, CTL_TITLE, s.connected ? L"Chat with the agent" : L"Chat with the agent (server not connected)");
        if (reconnected) Send("{\"type\":\"menu\",\"item\":\"chat_history\",\"id\":\"chat\"}");
    }
    if (HWND w = g_wnd[kLog]) {
        SetDlgItemTextW(w, CTL_TITLE, s.connected ? L"What the agent did" : L"What the agent did (server not connected)");
        if (reconnected) Send("{\"type\":\"menu\",\"item\":\"log\",\"id\":\"log\"}");
    }
    if (g_wnd[kPending] && s.connected && s.pending != g_pendingShownCount) {
        g_pendingShownCount = s.pending;
        Send("{\"type\":\"menu\",\"item\":\"pending\",\"id\":\"pending\"}");   // refresh the open list
    }
}

void OnCommand(UINT id) {
    auto s = State();
    switch (id) {
    case ID_PAUSE: Send(std::string("{\"type\":\"menu\",\"item\":\"") + (s.agent == "paused" ? "resume" : "pause") + "\"}"); break;
    case ID_CHAT:
        if (OpenTool(kChat)) FillChat();
        if (s.connected) Send("{\"type\":\"menu\",\"item\":\"chat_history\",\"id\":\"chat\"}");
        break;
    case ID_PENDING:
        g_pendingWanted = true;
        Send("{\"type\":\"menu\",\"item\":\"pending\",\"id\":\"pending\"}");
        break;
    case ID_SELECTION: {
        std::string line = live::Selection("");
        if (line.rfind("{\"type\":\"error\"", 0) == 0) Box(W(JStr(line, "message")), MB_OK | MB_ICONINFORMATION);
        else Send(line);
        break;
    }
    case ID_UNDO: Send("{\"type\":\"menu\",\"item\":\"undo_agent_step\"}"); break;
    case ID_LOG:
        if (OpenTool(kLog)) FillLog(true);
        if (s.connected) Send("{\"type\":\"menu\",\"item\":\"log\",\"id\":\"log\"}");
        break;
    case ID_STATUS: Box(StatusText(), MB_OK); break;
    case ID_DISCONNECT:
        if (s.connected) Send("{\"type\":\"menu\",\"item\":\"disconnect\"}");
        else SetEvent(g_stopEvent);   // no server: unload right away
        break;
    case ID_ABOUT: ShowAbout(); break;
    }
}

void Uninstall() {
    KillTimer(g_hwnd, kTimerId);
    UninstallUi();
    if (HMENU bar = GetMenu(g_hwnd))
        for (int i = GetMenuItemCount(bar) - 1; i >= 0; --i)
            if (GetSubMenu(bar, i) == g_menu) { RemoveMenu(bar, i, MF_BYPOSITION); DrawMenuBar(g_hwnd); }
    if (g_menu) { DestroyMenu(g_menu); g_menu = nullptr; }
}

LRESULT CALLBACK HookProc(HWND hwnd, UINT msg, WPARAM wp, LPARAM lp, UINT_PTR, DWORD_PTR) {
    switch (msg) {
    case WM_HMCP_INSTALL: InstallUi(); BuildMenu(); SetTimer(hwnd, kTimerId, 1000, nullptr); return 0;
    case WM_HMCP_UPDATE: OnStateChanged(); return 0;
    case WM_HMCP_LOG: FillLog(false); return 0;
    case WM_HMCP_LIVE: {
        auto r = reinterpret_cast<LiveRequest*>(lp);
        r->answer = r->camera ? live::Camera(r->id) : live::Selection(r->id);
        return 1;
    }
    case WM_HMCP_CHAT:
        // an answer from the agent opens the chat (without taking the focus from the map)
        if (lp == 1 && !g_wnd[kChat]) OpenTool(kChat, false);
        FillChat();
        return 0;
    case WM_HMCP_PENDING:
        if (g_pendingWanted && !g_wnd[kPending]) OpenTool(kPending);
        g_pendingWanted = false;
        FillPending();
        return 0;
    case WM_HMCP_UNINSTALL:
        Uninstall();
        // safe inside our own subclass proc and wherever we are in the chain
        g_subclassed = !RemoveWindowSubclass(hwnd, HookProc, kSubclassId);
        return 1;
    case WM_TIMER:
        if (wp == kTimerId) { BuildMenu(); return 0; }   // MDI frames swap menus when maps open / close
        break;
    case WM_COMMAND:
        if (HIWORD(wp) == 0 && LOWORD(wp) >= ID_PAUSE && LOWORD(wp) <= ID_ABOUT) { OnCommand(LOWORD(wp)); return 0; }
        break;
    case WM_INITMENUPOPUP: {
        LRESULT r = DefSubclassProc(hwnd, msg, wp, lp);
        if (reinterpret_cast<HMENU>(wp) == g_menu) UpdateMenu();
        return r;
    }
    }
    return DefSubclassProc(hwnd, msg, wp, lp);
}

// SetWindowSubclass works only on the window's own thread, and the worker finds the window on another one. So the
// worker puts this short-lived GWLP_WNDPROC proc in front and posts WM_HMCP_INSTALL; on the UI thread it takes itself
// out again (nobody had time to subclass on top of it) and installs the real, order-independent subclass.
// If someone did subclass on top of it meanwhile, it stays as a pass-through and the DLL is never unloaded.
LRESULT CALLBACK BootProc(HWND hwnd, UINT msg, WPARAM wp, LPARAM lp) {
    WNDPROC orig = g_origProc;
    if (msg == WM_HMCP_INSTALL && !g_subclassed && !g_bootDone) {
        g_bootDone = true;
        if (GetWindowLongPtrW(hwnd, GWLP_WNDPROC) == reinterpret_cast<LONG_PTR>(BootProc)) {
            SetWindowLongPtrW(hwnd, GWLP_WNDPROC, reinterpret_cast<LONG_PTR>(orig));
            g_bootInChain = false;
        }
        g_subclassed = SetWindowSubclass(hwnd, HookProc, kSubclassId, 0) != FALSE;
        // the subclass is in front now: deliver the message to it (installs the menu)
        return g_subclassed ? SendMessageW(hwnd, WM_HMCP_INSTALL, wp, lp) : 0;
    }
    return CallWindowProcW(orig, hwnd, msg, wp, lp);
}

// Hammer's main frame: a visible, unowned top-level window of this process with a menu bar; one whose title says
// "Hammer" or "Worldcraft" wins (GoldSrc editors are titled "Worldcraft - ..." / "Valve Hammer Editor - ...")
struct FindCtx { HWND named = nullptr, any = nullptr; };

BOOL CALLBACK FindProc(HWND h, LPARAM p) {
    auto ctx = reinterpret_cast<FindCtx*>(p);
    DWORD pid = 0;
    GetWindowThreadProcessId(h, &pid);
    if (pid != GetCurrentProcessId() || !IsWindowVisible(h) || GetWindow(h, GW_OWNER) || !GetMenu(h)) return TRUE;
    wchar_t title[256]{};
    GetWindowTextW(h, title, 256);
    if (wcsstr(title, L"Hammer") || wcsstr(title, L"Worldcraft")) { ctx->named = h; return FALSE; }
    if (!ctx->any) ctx->any = h;
    return TRUE;
}

HWND FindMainWindow() {
    FindCtx ctx;
    EnumWindows(FindProc, reinterpret_cast<LPARAM>(&ctx));
    return ctx.named ? ctx.named : ctx.any;
}

// ---------- pipe ----------

std::string Hello() {
    std::lock_guard l(g_lock);
    return std::string("{\"type\":\"hello\",\"adapter\":\"") + kAdapterId + "\",\"version\":\"" HMCP_NATIVE_VERSION "\",\"pid\":" +
           std::to_string(GetCurrentProcessId()) + ",\"build\":\"" + g_build + "\",\"support\":\"" + g_support +
           "\",\"editorSha256\":\"" + g_sha + "\"}";
}

bool WriteAll(HANDLE pipe, const std::string& line) {
    std::string s = line + "\n";
    OVERLAPPED ov{};
    ov.hEvent = CreateEventW(nullptr, TRUE, FALSE, nullptr);
    DWORD n = 0;
    BOOL ok = WriteFile(pipe, s.data(), (DWORD)s.size(), nullptr, &ov);
    if (ok || GetLastError() == ERROR_IO_PENDING) ok = GetOverlappedResult(pipe, &ov, &n, TRUE) && n == s.size();
    CloseHandle(ov.hEvent);
    return ok;
}

// returns true when the adapter should unload
bool HandleLine(HANDLE pipe, const std::string& msg) {
    std::string cmd = JStr(msg, "cmd");
    auto chatLine = [](const std::string& o) { return ChatLine{ JStr(o, "from", "agent"), JStr(o, "text"), JStr(o, "at") }; };
    if (cmd == "ping") WriteAll(pipe, "{\"type\":\"pong\"}");
    else if (cmd == "unload") { WriteAll(pipe, "{\"type\":\"bye\"}"); return true; }
    else if (cmd == "state") {
        {
            std::lock_guard l(g_lock);
            g_state.server = JStr(msg, "server", g_state.server);
            g_state.mode = JStr(msg, "mode", g_state.mode);
            g_state.agent = JStr(msg, "agent", g_state.agent);
            g_state.approval = JStr(msg, "approval", g_state.approval);
            g_state.pending = JInt(msg, "pending", g_state.pending);
            g_state.canUndo = JBool(msg, "canUndo", g_state.canUndo);
        }
        PostUi(WM_HMCP_UPDATE);
    } else if (cmd == "profile") {
        // the server's registry entry for our build (layer 2 signatures / offsets come in "features"); a profile for
        // another build is ignored - the adapter's own sha256 check is what says which build this is
        std::string build = JStr(msg, "build");
        std::lock_guard l(g_lock);
        if (build == g_build) {
            g_profile = msg;
            g_support = JStr(msg, "support", g_support);
            live::Load(msg);
        }
    } else if (cmd == "log") {
        {
            std::lock_guard l(g_lock);
            auto lines = JStrings(JArray(msg, "lines"));
            size_t from = lines.size() > kLogMax ? lines.size() - kLogMax : 0;
            g_log.assign(lines.begin() + from, lines.end());
            g_logReset = true;
        }
        PostUi(WM_HMCP_LOG);
    } else if (cmd == "log_line") {
        {
            std::lock_guard l(g_lock);
            std::string line = JStr(msg, "line");
            g_log.push_back(line);
            if (g_log.size() > kLogMax) g_log.pop_front();
            g_logNew.push_back(line);
        }
        PostUi(WM_HMCP_LOG);
    } else if (cmd == "chat") {
        ChatLine c = chatLine(msg);
        {
            std::lock_guard l(g_lock);
            g_chat.push_back(c);
            if (g_chat.size() > kChatMax) g_chat.pop_front();
        }
        PostUi(WM_HMCP_CHAT, c.from == "agent" ? 1 : 0);
    } else if (cmd == "chat_history") {
        {
            std::lock_guard l(g_lock);
            g_chat.clear();
            for (auto& o : JObjects(JArray(msg, "items"))) g_chat.push_back(chatLine(o));
            while (g_chat.size() > kChatMax) g_chat.pop_front();
        }
        PostUi(WM_HMCP_CHAT);
    } else if (cmd == "pending") {
        {
            std::lock_guard l(g_lock);
            g_pendingItems.clear();
            for (auto& o : JObjects(JArray(msg, "items"))) g_pendingItems.emplace_back(JStr(o, "id"), JStr(o, "text"));
        }
        PostUi(WM_HMCP_PENDING);
    } else if (cmd == "get_selection" || cmd == "get_camera") {
        // read on Hammer's UI thread (the editor changes these structures there); a busy UI = no answer in time
        LiveRequest r{ cmd == "get_camera", JStr(msg, "id"), {} };
        DWORD_PTR done = 0;
        if (!g_hwnd || !SendMessageTimeoutW(g_hwnd, WM_HMCP_LIVE, 0, reinterpret_cast<LPARAM>(&r), SMTO_NORMAL, 3000, &done) || !done)
            r.answer = "{\"type\":\"error\",\"id\":\"" + JEsc(r.id) + "\",\"message\":\"Hammer is busy (no answer from its window in 3 s)\"}";
        WriteAll(pipe, r.answer);
    } else if (cmd == "notify") {
        { std::lock_guard l(g_lock); g_state.notify = JStr(msg, "text"); }
        if (g_hwnd) FlashWindow(g_hwnd, TRUE);
    } else {
        WriteAll(pipe, "{\"type\":\"error\",\"message\":\"unknown command\"}");
    }
    return false;
}

void SetConnected(bool on) {
    {
        std::lock_guard l(g_lock);
        if (!on) g_state = ServerState{};
        g_state.connected = on;
    }
    PostUi(WM_HMCP_UPDATE);
}

// one client session; true = unload requested
bool Session(HANDLE pipe) {
    { std::lock_guard l(g_lock); g_out.clear(); }
    SetConnected(true);
    if (!WriteAll(pipe, Hello())) { SetConnected(false); return false; }
    OVERLAPPED rov{};
    rov.hEvent = CreateEventW(nullptr, TRUE, FALSE, nullptr);
    char chunk[4096];
    std::string buf;
    bool unload = false, alive = true;
    auto startRead = [&] {
        ResetEvent(rov.hEvent);
        if (!ReadFile(pipe, chunk, sizeof chunk, nullptr, &rov) && GetLastError() != ERROR_IO_PENDING) alive = false;
    };
    startRead();
    while (alive && !unload) {
        HANDLE waits[] = { rov.hEvent, g_outEvent, g_stopEvent };
        DWORD w = WaitForMultipleObjects(3, waits, FALSE, INFINITE);
        if (w == WAIT_OBJECT_0) {
            DWORD n = 0;
            if (!GetOverlappedResult(pipe, &rov, &n, FALSE) || n == 0) { alive = false; break; }
            buf.append(chunk, n);
            for (size_t eol; (eol = buf.find('\n')) != std::string::npos; buf.erase(0, eol + 1))
                if (HandleLine(pipe, buf.substr(0, eol))) { unload = true; break; }
            if (!unload) startRead();
        } else if (w == WAIT_OBJECT_0 + 1) {
            std::deque<std::string> out;
            { std::lock_guard l(g_lock); out.swap(g_out); }
            for (auto& line : out)
                if (!WriteAll(pipe, line)) { alive = false; break; }
        } else {
            unload = true;
        }
    }
    CancelIoEx(pipe, &rov);
    CloseHandle(rov.hEvent);
    SetConnected(false);
    return unload;
}

HANDLE g_uiStop = nullptr;   // tells the window finder to give up (unloading)

DWORD WINAPI FindWindowThread(LPVOID) {
    for (int i = 0; i < 1200; ++i) {   // up to 10 minutes: Hammer may sit in "Reading chunks..." for a long time
        if (HWND h = FindMainWindow()) {
            g_hwnd = h;
            g_origProc = reinterpret_cast<WNDPROC>(GetWindowLongPtrW(h, GWLP_WNDPROC));
            g_bootInChain = true;
            SetWindowLongPtrW(h, GWLP_WNDPROC, reinterpret_cast<LONG_PTR>(BootProc));
            PostMessageW(h, WM_HMCP_INSTALL, 0, 0);
            return 0;
        }
        if (WaitForSingleObject(g_uiStop, 500) == WAIT_OBJECT_0) return 0;
    }
    return 0;
}

// Pipe access: only the user Hammer runs as (and SYSTEM) - the default DACL would also give Everyone read access.
// Uses the user's SID rather than OWNER RIGHTS, so an elevated Hammer still accepts the same user's non-elevated app.
PSECURITY_DESCRIPTOR PipeSecurity() {
    HANDLE token = nullptr;
    if (!OpenProcessToken(GetCurrentProcess(), TOKEN_QUERY, &token)) return nullptr;
    PSECURITY_DESCRIPTOR sd = nullptr;
    BYTE buf[256]; DWORD n = 0;
    if (GetTokenInformation(token, TokenUser, buf, sizeof buf, &n)) {
        LPWSTR sid = nullptr;
        if (ConvertSidToStringSidW(reinterpret_cast<TOKEN_USER*>(buf)->User.Sid, &sid)) {
            std::wstring sddl = L"D:P(A;;GA;;;" + std::wstring(sid) + L")(A;;GA;;;SY)";
            ConvertStringSecurityDescriptorToSecurityDescriptorW(sddl.c_str(), SDDL_REVISION_1, &sd, nullptr);
            LocalFree(sid);
        }
    }
    CloseHandle(token);
    return sd;   // nullptr = default DACL (still local-only)
}

DWORD WINAPI Worker(LPVOID) {
    // one adapter per process, even when a copy is injected from another folder (another module)
    HANDLE instance = CreateMutexW(nullptr, TRUE, hmcp::InstanceName(GetCurrentProcessId()).c_str());
    if (!instance || GetLastError() == ERROR_ALREADY_EXISTS) {
        if (instance) CloseHandle(instance);
        CloseHandle(g_outEvent);
        CloseHandle(g_stopEvent);
        CloseHandle(g_uiStop);
        FreeLibraryAndExitThread(g_self, 0);
    }

    // build / sha of the editor this adapter runs in (for hello, Status and About)
    wchar_t exe[MAX_PATH * 2];
    GetModuleFileNameW(nullptr, exe, MAX_PATH * 2);
    // which registry build this process is: the allowed build of this bitness whose editor file hash matches
    std::vector<std::pair<std::wstring, std::string>> hashed;
    for (const HmcpAllowedBuild* b = kHmcpAllowedBuilds; b->id; ++b) {
        if (b->x64 != (sizeof(void*) == 8)) continue;
        // GoldSrc: the editor is this very exe (never a neighbour such as wc.exe next to hammer.exe)
        std::wstring dll = hmcp::EndsWithExe(b->editorDll) ? std::wstring(exe) : hmcp::EditorDll(exe, b->editorDll);
        if (dll.empty()) continue;
        std::string sha;
        for (auto& [path, h] : hashed) if (path == dll) sha = h;
        if (sha.empty()) { sha = hmcp::Sha256File(dll); hashed.emplace_back(dll, sha); }
        if (sha == b->sha256) { std::lock_guard l(g_lock); g_build = b->id; g_sha = sha; g_support = b->support; break; }
    }

    // Hammer's main window may not exist yet (injected during start-up, or Hammer busy loading a map for minutes):
    // a separate thread looks for it, so the pipe is up for the server right away
    HANDLE finder = CreateThread(nullptr, 0, FindWindowThread, nullptr, 0, nullptr);

    std::wstring name = hmcp::PipeName(GetCurrentProcessId());
    PSECURITY_DESCRIPTOR sd = PipeSecurity();
    SECURITY_ATTRIBUTES sa{ sizeof sa, sd, FALSE };
    bool unload = false;
    int failures = 0;   // CreateNamedPipe failures in a row
    while (!unload) {
        HANDLE pipe = CreateNamedPipeW(name.c_str(), PIPE_ACCESS_DUPLEX | FILE_FLAG_OVERLAPPED,
            PIPE_TYPE_BYTE | PIPE_READMODE_BYTE | PIPE_WAIT | PIPE_REJECT_REMOTE_CLIENTS, 1, 8192, 8192, 0, sd ? &sa : nullptr);
        if (pipe == INVALID_HANDLE_VALUE) {
            // never unload silently because of a transient failure (e.g. the old instance still closing):
            // retry for ~10 s, then give up and unload
            if (++failures > 100 || WaitForSingleObject(g_stopEvent, 100) == WAIT_OBJECT_0) break;
            continue;
        }
        failures = 0;
        OVERLAPPED cov{};
        cov.hEvent = CreateEventW(nullptr, TRUE, FALSE, nullptr);
        bool connected = ConnectNamedPipe(pipe, &cov) != 0;
        if (!connected) {
            DWORD e = GetLastError();
            if (e == ERROR_PIPE_CONNECTED) connected = true;
            else if (e == ERROR_IO_PENDING) {
                HANDLE waits[] = { cov.hEvent, g_stopEvent };
                if (WaitForMultipleObjects(2, waits, FALSE, INFINITE) == WAIT_OBJECT_0) {
                    DWORD n;
                    connected = GetOverlappedResult(pipe, &cov, &n, FALSE) != 0;
                } else {
                    CancelIoEx(pipe, &cov);
                    unload = true;
                }
            }
        }
        CloseHandle(cov.hEvent);
        if (connected) unload = Session(pipe);
        FlushFileBuffers(pipe);
        DisconnectNamedPipe(pipe);
        CloseHandle(pipe);
    }

    // stop looking for the window, then uninstall the UI on the UI thread after our dialogs are closed;
    // only then release the instance and unload the code all that runs
    SetEvent(g_uiStop);
    if (finder) { WaitForSingleObject(finder, INFINITE); CloseHandle(finder); }
    if (g_hwnd) {
        while (g_dialogs > 0) Sleep(100);
        SendMessageW(g_hwnd, WM_HMCP_UNINSTALL, 0, 0);   // removes the subclass (UI thread)
        // the boot proc never got its message (or somebody subclassed on top of it): take it out if it is on top
        if (g_bootInChain && GetWindowLongPtrW(g_hwnd, GWLP_WNDPROC) == reinterpret_cast<LONG_PTR>(BootProc)) {
            SetWindowLongPtrW(g_hwnd, GWLP_WNDPROC, reinterpret_cast<LONG_PTR>(g_origProc));
            g_bootInChain = false;
        }
    }
    CloseHandle(g_outEvent);
    CloseHandle(g_stopEvent);
    CloseHandle(g_uiStop);
    if (sd) LocalFree(sd);
    ReleaseMutex(instance);
    CloseHandle(instance);
    // BootProc still inside someone else's chain: keep the code loaded (an idle pass-through) rather than crash Hammer
    if (g_bootInChain || g_subclassed) ExitThread(0);
    FreeLibraryAndExitThread(g_self, 0);
}

} // namespace

BOOL WINAPI DllMain(HINSTANCE self, DWORD reason, LPVOID) {
    if (reason == DLL_PROCESS_ATTACH) {
        g_self = self;
        DisableThreadLibraryCalls(self);
        g_outEvent = CreateEventW(nullptr, FALSE, FALSE, nullptr);
        g_stopEvent = CreateEventW(nullptr, TRUE, FALSE, nullptr);
        g_uiStop = CreateEventW(nullptr, TRUE, FALSE, nullptr);
        // nothing heavy under the loader lock: the worker thread does the rest
        if (HANDLE t = CreateThread(nullptr, 0, Worker, nullptr, 0, nullptr)) CloseHandle(t);
    }
    return TRUE;
}
