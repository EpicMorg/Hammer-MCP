// Hammer-MCP adapter. HMCP_ADAPTER_ID is the registry "injectAdapter" it is built for:
//   generic-x86 / generic-x64 - every Source 1 Hammer with a Win32 menu (stock, Hammer++, Slammin, Black Mesa, ...);
//   hpp-8873-x86 / hpp-8873-x64 - Hammer++ 8873, which will also get build-specific code from layer 2 on.
// Layers 0-1 touch no Hammer internals (window, menu, pipe), so one source serves every id.
// Layer 0: named pipe, hello, ping, unload.  Layer 1: the MCP menu in Hammer's main window.
// Protocol: see src/native/PROTOCOL.md. One JSON object per line over \\.\pipe\hammer-mcp-adapter-<pid> (local only).
//
// Threads: DllMain only starts the worker. The worker owns the pipe (overlapped I/O: reading and writing at the same
// time) and finds Hammer's main window; everything that touches the UI runs on Hammer's UI thread through our
// subclassed window procedure (custom WM_APP messages). Unload: UI is uninstalled on the UI thread first (menu removed,
// window procedure restored, waiting for our dialogs to close), then FreeLibraryAndExitThread.
#include "hmcp.h"
#include <deque>
#include <mutex>
#include <string>
#include <vector>

namespace {

#ifndef HMCP_ADAPTER_ID
#error "HMCP_ADAPTER_ID is set by CMake"
#endif
constexpr const char* kAdapterId = HMCP_ADAPTER_ID;

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

HMODULE g_self = nullptr;
std::mutex g_lock;            // guards g_state, g_out, g_log, g_pendingItems, g_build, g_sha
ServerState g_state;
std::deque<std::string> g_out;
std::vector<std::string> g_log;
std::vector<std::pair<std::string, std::string>> g_pendingItems;  // id, text
std::string g_build = "unknown", g_sha;
HANDLE g_outEvent = nullptr;   // something queued in g_out
HANDLE g_stopEvent = nullptr;  // unload without a server (menu "Disconnect" while not connected)

// UI: g_hwnd / g_origProc are set by the worker before subclassing; the rest is touched on the UI thread only
HWND g_hwnd = nullptr;
WNDPROC g_origProc = nullptr;
HMENU g_menu = nullptr;
volatile LONG g_dialogs = 0;   // our modal dialogs open right now

constexpr UINT WM_HMCP_INSTALL = WM_APP + 0x1E0;
constexpr UINT WM_HMCP_UPDATE = WM_APP + 0x1E1;
constexpr UINT WM_HMCP_SHOWLOG = WM_APP + 0x1E2;
constexpr UINT WM_HMCP_UNINSTALL = WM_APP + 0x1E3;
constexpr UINT WM_HMCP_SHOWPENDING = WM_APP + 0x1E4;
constexpr UINT_PTR kTimerId = 0xBE5F;

// command ids far above Hammer's own (its highest are around 0x8100)
enum : UINT {
    ID_PAUSE = 0xBE00, ID_SELECTION, ID_ASK, ID_PENDING, ID_UNDO, ID_LOG, ID_STATUS, ID_DISCONNECT, ID_ABOUT,
};

void Send(const std::string& line) {
    { std::lock_guard l(g_lock); g_out.push_back(line); }
    SetEvent(g_outEvent);
}

ServerState State() { std::lock_guard l(g_lock); return g_state; }

void PostUi(UINT msg) { if (g_hwnd) PostMessageW(g_hwnd, msg, 0, 0); }

// ---------- dialogs ----------

// in-memory dialog template: "Ask agent" with a multi-line edit box, Send / Cancel
struct AskDialog {
    std::vector<WORD> t;
    void Align() { while ((t.size() * sizeof(WORD)) % 4) t.push_back(0); }
    void Str(const wchar_t* s) { for (; *s; ++s) t.push_back(*s); t.push_back(0); }
    void Item(DWORD style, short x, short y, short cx, short cy, WORD id, WORD cls, const wchar_t* text) {
        Align();
        DLGITEMTEMPLATE it{ style | WS_CHILD | WS_VISIBLE, 0, x, y, cx, cy, id };
        auto p = reinterpret_cast<WORD*>(&it);
        t.insert(t.end(), p, p + sizeof it / sizeof(WORD));
        t.push_back(0xFFFF); t.push_back(cls);
        Str(text);
        t.push_back(0);   // no creation data
    }
    AskDialog() {
        DLGTEMPLATE d{ DS_MODALFRAME | DS_CENTER | DS_SETFONT | WS_POPUP | WS_CAPTION | WS_SYSMENU, 0, 4, 0, 0, 280, 120 };
        auto p = reinterpret_cast<WORD*>(&d);
        t.insert(t.end(), p, p + sizeof d / sizeof(WORD));
        t.push_back(0); t.push_back(0);   // no menu, default class
        Str(L"Hammer-MCP: ask the agent");
        t.push_back(9); Str(L"Segoe UI");
        Item(SS_LEFT, 7, 7, 266, 10, 0xFFFF, 0x0082, L"Your request goes to the agent with its next tool call:");
        Item(ES_MULTILINE | ES_AUTOVSCROLL | ES_WANTRETURN | WS_BORDER | WS_TABSTOP | WS_VSCROLL, 7, 20, 266, 70, 100, 0x0081, L"");
        Item(BS_DEFPUSHBUTTON | WS_TABSTOP, 164, 98, 52, 15, IDOK, 0x0080, L"Send");
        Item(BS_PUSHBUTTON | WS_TABSTOP, 221, 98, 52, 15, IDCANCEL, 0x0080, L"Cancel");
    }
};

INT_PTR CALLBACK AskProc(HWND dlg, UINT msg, WPARAM wp, LPARAM lp) {
    switch (msg) {
    case WM_INITDIALOG:
        SetWindowLongPtrW(dlg, DWLP_USER, lp);
        SetFocus(GetDlgItem(dlg, 100));
        return FALSE;
    case WM_COMMAND:
        if (LOWORD(wp) == IDOK) {
            HWND edit = GetDlgItem(dlg, 100);
            std::wstring text(GetWindowTextLengthW(edit) + 1, L'\0');
            text.resize(GetWindowTextW(edit, text.data(), (int)text.size()));
            *reinterpret_cast<std::wstring*>(GetWindowLongPtrW(dlg, DWLP_USER)) = text;
            EndDialog(dlg, IDOK);
            return TRUE;
        }
        if (LOWORD(wp) == IDCANCEL) { EndDialog(dlg, IDCANCEL); return TRUE; }
        break;
    }
    return FALSE;
}

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
    std::string sha, build;
    { std::lock_guard l(g_lock); sha = g_sha; build = g_build; }
    std::wstring t = L"Adapter: " + W(kAdapterId) + L" " + W(HMCP_NATIVE_VERSION) + L"\n";
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

void ShowLog() {
    std::vector<std::string> lines;
    { std::lock_guard l(g_lock); lines = g_log; }
    std::wstring t;
    size_t from = lines.size() > 40 ? lines.size() - 40 : 0;   // a message box, not a viewer: the newest 40
    for (size_t i = from; i < lines.size(); ++i) t += W(lines[i]) + L"\n";
    Box(t.empty() ? L"The agent has not done anything yet." : L"Agent activity (newest last):\n\n" + t, MB_OK);
}

void ShowPending() {
    std::vector<std::pair<std::string, std::string>> items;
    { std::lock_guard l(g_lock); items = g_pendingItems; }
    if (items.empty()) { Box(L"No pending changes.", MB_OK); return; }
    std::wstring t = L"The agent wants to make these changes:\n\n";
    for (auto& [id, text] : items) t += L"- " + W(text) + L"\n";
    t += L"\nYes = apply all, No = reject all, Cancel = decide later.";
    int r = Box(t, MB_YESNOCANCEL);
    if (r == IDCANCEL) return;
    std::string ids;
    for (auto& [id, text] : items) ids += (ids.empty() ? "\"" : ",\"") + JEsc(id) + "\"";
    Send(r == IDYES ? "{\"type\":\"approval\",\"apply\":[" + ids + "],\"reject\":[]}"
                    : "{\"type\":\"approval\",\"apply\":[],\"reject\":[" + ids + "]}");
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
        AppendMenuW(g_menu, MF_STRING, ID_ASK, L"Ask agent...");
        AppendMenuW(g_menu, MF_STRING, ID_PENDING, L"Pending changes...");
        AppendMenuW(g_menu, MF_STRING, ID_UNDO, L"Undo agent's last step");
        AppendMenuW(g_menu, MF_SEPARATOR, 0, nullptr);
        AppendMenuW(g_menu, MF_STRING, ID_LOG, L"Activity log...");
        AppendMenuW(g_menu, MF_STRING, ID_STATUS, L"Status...");
        AppendMenuW(g_menu, MF_STRING, ID_DISCONNECT, L"Disconnect adapter");
        AppendMenuW(g_menu, MF_SEPARATOR, 0, nullptr);
        AppendMenuW(g_menu, MF_STRING, ID_ABOUT, L"About Hammer-MCP adapter...");
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
    ModifyMenuW(g_menu, ID_PENDING, MF_BYCOMMAND | MF_STRING, ID_PENDING, pending.c_str());
    enable(ID_PAUSE, on);
    enable(ID_SELECTION, false);   // layer 2
    enable(ID_ASK, on);
    enable(ID_PENDING, on && s.approval == "ask" && s.pending > 0);
    enable(ID_UNDO, on && s.canUndo);
    enable(ID_LOG, on);
    enable(ID_STATUS, true);
    enable(ID_DISCONNECT, true);
    enable(ID_ABOUT, true);
}

void OnCommand(UINT id) {
    auto s = State();
    switch (id) {
    case ID_PAUSE: Send(std::string("{\"type\":\"menu\",\"item\":\"") + (s.agent == "paused" ? "resume" : "pause") + "\"}"); break;
    case ID_ASK: {
        static AskDialog tpl;
        std::wstring text;
        InterlockedIncrement(&g_dialogs);
        INT_PTR r = DialogBoxIndirectParamW(g_self, reinterpret_cast<LPCDLGTEMPLATEW>(tpl.t.data()), g_hwnd, AskProc,
                                            reinterpret_cast<LPARAM>(&text));
        InterlockedDecrement(&g_dialogs);
        if (r == IDOK && !text.empty()) Send("{\"type\":\"ask\",\"text\":\"" + JEsc(U8(text)) + "\"}");
        break;
    }
    case ID_PENDING: Send("{\"type\":\"menu\",\"item\":\"pending\",\"id\":\"pending\"}"); break;
    case ID_UNDO: Send("{\"type\":\"menu\",\"item\":\"undo_agent_step\"}"); break;
    case ID_LOG: Send("{\"type\":\"menu\",\"item\":\"log\",\"id\":\"log\"}"); break;
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
    if (HMENU bar = GetMenu(g_hwnd))
        for (int i = GetMenuItemCount(bar) - 1; i >= 0; --i)
            if (GetSubMenu(bar, i) == g_menu) { RemoveMenu(bar, i, MF_BYPOSITION); DrawMenuBar(g_hwnd); }
    if (g_menu) { DestroyMenu(g_menu); g_menu = nullptr; }
    if (g_origProc) SetWindowLongPtrW(g_hwnd, GWLP_WNDPROC, reinterpret_cast<LONG_PTR>(g_origProc));
}

LRESULT CALLBACK HookProc(HWND hwnd, UINT msg, WPARAM wp, LPARAM lp) {
    switch (msg) {
    case WM_HMCP_INSTALL: BuildMenu(); SetTimer(hwnd, kTimerId, 1000, nullptr); return 0;
    case WM_HMCP_UPDATE: if (g_menu) UpdateMenu(); return 0;
    case WM_HMCP_SHOWLOG: ShowLog(); return 0;
    case WM_HMCP_SHOWPENDING: ShowPending(); return 0;
    case WM_HMCP_UNINSTALL: Uninstall(); return 1;
    case WM_TIMER:
        if (wp == kTimerId) { BuildMenu(); return 0; }   // MDI frames swap menus when maps open / close
        break;
    case WM_COMMAND:
        if (HIWORD(wp) == 0 && LOWORD(wp) >= ID_PAUSE && LOWORD(wp) <= ID_ABOUT) { OnCommand(LOWORD(wp)); return 0; }
        break;
    case WM_INITMENUPOPUP: {
        LRESULT r = CallWindowProcW(g_origProc, hwnd, msg, wp, lp);
        if (reinterpret_cast<HMENU>(wp) == g_menu) UpdateMenu();
        return r;
    }
    }
    return CallWindowProcW(g_origProc, hwnd, msg, wp, lp);
}

// Hammer's main frame: a visible top-level window of this process with a menu bar and "Hammer" in the title
BOOL CALLBACK FindProc(HWND h, LPARAM p) {
    DWORD pid = 0;
    GetWindowThreadProcessId(h, &pid);
    if (pid != GetCurrentProcessId() || !IsWindowVisible(h) || GetWindow(h, GW_OWNER) || !GetMenu(h)) return TRUE;
    wchar_t title[256]{};
    GetWindowTextW(h, title, 256);
    if (wcsstr(title, L"Hammer")) { *reinterpret_cast<HWND*>(p) = h; return FALSE; }
    return TRUE;
}

HWND FindMainWindow() {
    HWND found = nullptr;
    EnumWindows(FindProc, reinterpret_cast<LPARAM>(&found));
    return found;
}

// ---------- pipe ----------

std::string Hello() {
    std::lock_guard l(g_lock);
    return std::string("{\"type\":\"hello\",\"adapter\":\"") + kAdapterId + "\",\"version\":\"" HMCP_NATIVE_VERSION "\",\"pid\":" +
           std::to_string(GetCurrentProcessId()) + ",\"build\":\"" + g_build + "\",\"editorSha256\":\"" + g_sha + "\"}";
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
    } else if (cmd == "log") {
        { std::lock_guard l(g_lock); g_log = JStrings(JArray(msg, "lines")); }
        PostUi(WM_HMCP_SHOWLOG);
    } else if (cmd == "pending") {
        {
            std::lock_guard l(g_lock);
            g_pendingItems.clear();
            for (auto& o : JObjects(JArray(msg, "items"))) g_pendingItems.emplace_back(JStr(o, "id"), JStr(o, "text"));
        }
        PostUi(WM_HMCP_SHOWPENDING);
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

DWORD WINAPI Worker(LPVOID) {
    // build / sha of the editor this adapter runs in (for hello, Status and About)
    wchar_t exe[MAX_PATH * 2];
    GetModuleFileNameW(nullptr, exe, MAX_PATH * 2);
    // which registry build this process is: the first allowed build of this adapter id whose editor DLL hash matches
    std::vector<std::pair<std::wstring, std::string>> hashed;
    for (const HmcpAllowedBuild* b = kHmcpAllowedBuilds; b->id; ++b) {
        if (std::string(b->adapter) != kAdapterId) continue;
        std::wstring dll = hmcp::EditorDll(exe, b->editorDll);
        if (dll.empty()) continue;
        std::string sha;
        for (auto& [path, h] : hashed) if (path == dll) sha = h;
        if (sha.empty()) { sha = hmcp::Sha256File(dll); hashed.emplace_back(dll, sha); }
        if (sha == b->sha256) { std::lock_guard l(g_lock); g_build = b->id; g_sha = sha; break; }
    }

    // Hammer's main window may not exist yet (injected during start-up): look for it, then subclass and add the menu
    for (int i = 0; i < 600 && !g_hwnd; ++i) {
        if (HWND h = FindMainWindow()) {
            g_origProc = reinterpret_cast<WNDPROC>(GetWindowLongPtrW(h, GWLP_WNDPROC));
            g_hwnd = h;
            SetWindowLongPtrW(h, GWLP_WNDPROC, reinterpret_cast<LONG_PTR>(HookProc));
            PostMessageW(h, WM_HMCP_INSTALL, 0, 0);
            break;
        }
        if (WaitForSingleObject(g_stopEvent, 500) == WAIT_OBJECT_0) break;
    }

    std::wstring name = hmcp::PipeName(GetCurrentProcessId());
    bool unload = WaitForSingleObject(g_stopEvent, 0) == WAIT_OBJECT_0;
    while (!unload) {
        HANDLE pipe = CreateNamedPipeW(name.c_str(), PIPE_ACCESS_DUPLEX | FILE_FLAG_OVERLAPPED,
            PIPE_TYPE_BYTE | PIPE_READMODE_BYTE | PIPE_WAIT | PIPE_REJECT_REMOTE_CLIENTS, 1, 8192, 8192, 0, nullptr);
        if (pipe == INVALID_HANDLE_VALUE) break;
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

    // uninstall the UI on the UI thread, after our dialogs are closed; only then unload the code they run
    if (g_hwnd) {
        while (g_dialogs > 0) Sleep(100);
        SendMessageW(g_hwnd, WM_HMCP_UNINSTALL, 0, 0);
    }
    CloseHandle(g_outEvent);
    CloseHandle(g_stopEvent);
    FreeLibraryAndExitThread(g_self, 0);
}

} // namespace

BOOL WINAPI DllMain(HINSTANCE self, DWORD reason, LPVOID) {
    if (reason == DLL_PROCESS_ATTACH) {
        g_self = self;
        DisableThreadLibraryCalls(self);
        g_outEvent = CreateEventW(nullptr, FALSE, FALSE, nullptr);
        g_stopEvent = CreateEventW(nullptr, TRUE, FALSE, nullptr);
        // nothing heavy under the loader lock: the worker thread does the rest
        if (HANDLE t = CreateThread(nullptr, 0, Worker, nullptr, 0, nullptr)) CloseHandle(t);
    }
    return TRUE;
}
