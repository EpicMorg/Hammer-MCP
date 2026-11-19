// Layer 2 reader, see live.h. The layout is data from the server; nothing here knows a particular build.
#include "live.h"
#include <windows.h>
#include <cmath>
#include <cstdint>
#include <cstdio>
#include <cstdlib>
#include <map>
#include <mutex>
#include <set>
#include <vector>

namespace live {
namespace {

using uptr = uintptr_t;

std::mutex g_lock;
std::map<std::string, std::string> g_layout;   // feature name -> value as sent ("0x…" or text)
uptr g_base = 0;                                // the module the addresses are relative to

// a feature works only when the profile names every field it reads (a missing one would read offset 0)
const char* const kSelectionKeys[] = { "doc.selection", "object.mins", "object.maxs", "object.origin", "object.parent",
    "entity.vtable", "entity.className", "entity.keyValues", "entity.keyValueCount", "entity.keyValueSize", "entity.keyLength",
    "entity.valueOffset", "entity.valueLength", "solid.vtable", "solid.faces", "face.size", "face.texture" };
// Source Hammer ("layout": "source"): the document points to a CSelection whose CUtlVector lists the objects, an
// entity's keyvalues are a CUtlDict (red-black tree of MDkeyvalue nodes), a solid's faces are a CUtlVector of CMapFace
const char* const kSourceSelectionKeys[] = { "doc.selection", "selection.list", "object.mins", "object.maxs", "object.origin",
    "object.parent", "entity.vtable", "entity.className", "entity.keyValues", "kv.nodes", "kv.alloc", "kv.root", "kv.nodeSize",
    "kv.left", "kv.right", "kv.key", "kv.keyLength", "kv.value", "kv.valueLength", "solid.vtable", "solid.faces",
    "solid.faceCount", "face.size", "face.texture" };
const char* const kCameraKeys[] = { "doc.views", "view3d.vtable", "view3d.camera", "camera.position" };
bool g_source = false;
bool g_selection = false, g_camera = false;
std::string g_reason;   // why live reading is off (shown in Status)

// lengths and strides from the profile go into allocations and address arithmetic: a typo in a user's registry must
// switch the feature off, not throw inside Hammer's window procedure
const char* const kLengths[] = { "entity.classNameLength", "entity.keyLength", "entity.valueLength", "face.textureLength",
    "kv.keyLength", "kv.valueLength" };
const char* const kStrides[] = { "entity.keyValueSize", "face.size", "kv.nodeSize" };
constexpr uintptr_t kMaxLength = 4096, kMaxStride = 64 * 1024;

constexpr size_t kMaxItems = 200, kMaxKeys = 128, kMaxFaces = 512, kMaxList = 100000;

// ---------- layout ----------

bool Has(const char* k) { return g_layout.count(k) > 0; }
uptr Num(const char* k) {
    auto it = g_layout.find(k);
    return it == g_layout.end() ? 0 : (uptr)strtoull(it->second.c_str(), nullptr, 0);
}
uptr Rva(const char* k) { return g_base + Num(k); }

// the flat "features" object of the profile line: "name": "value" pairs
std::map<std::string, std::string> ParseFeatures(const std::string& line) {
    std::map<std::string, std::string> out;
    size_t p = line.find("\"features\"");
    if (p == std::string::npos) return out;
    p = line.find('{', p);
    if (p == std::string::npos) return out;
    std::vector<std::string> strs;
    for (size_t i = p + 1; i < line.size() && line[i] != '}'; ++i) {
        if (line[i] != '"') continue;
        std::string s;
        for (++i; i < line.size() && line[i] != '"'; ++i) {
            if (line[i] == '\\' && i + 1 < line.size()) ++i;
            s += line[i];
        }
        strs.push_back(s);
    }
    for (size_t i = 0; i + 1 < strs.size(); i += 2) out[strs[i]] = strs[i + 1];
    return out;
}

// ---------- safe reads ----------

bool Rd(uptr a, void* out, size_t n) {
    if (a < 0x10000) return false;
    SIZE_T got = 0;
    return ReadProcessMemory(GetCurrentProcess(), reinterpret_cast<LPCVOID>(a), out, n, &got) && got == n;
}
template <class T> bool Rd(uptr a, T& v) { return Rd(a, &v, sizeof v); }
uptr Ptr(uptr a) { uptr v = 0; return Rd(a, v) ? v : 0; }

// a fixed char[] field (editor strings are ANSI) as UTF-8
std::string Str(uptr a, size_t max) {
    if (!max || max > kMaxLength) return {};
    std::vector<char> b(max + 1, 0);
    if (!Rd(a, b.data(), max)) return {};
    size_t n = strnlen(b.data(), max);
    int wn = MultiByteToWideChar(CP_ACP, 0, b.data(), (int)n, nullptr, 0);
    std::wstring w(wn, L'\0');
    MultiByteToWideChar(CP_ACP, 0, b.data(), (int)n, w.data(), wn);
    int un = WideCharToMultiByte(CP_UTF8, 0, w.data(), wn, nullptr, 0, nullptr, nullptr);
    std::string u(un, '\0');
    WideCharToMultiByte(CP_UTF8, 0, w.data(), wn, u.data(), un, nullptr, nullptr);
    return u;
}

// Valve CUtlVector<T*>: CUtlMemory { T* memory; int allocated; int grow; } then int size (x86 +0xc, x64 +0x10)
std::vector<uptr> UtlVector(uptr vec) {
    std::vector<uptr> out;
    uptr mem = Ptr(vec);
    int count = 0;
    if (!mem || !Rd(vec + sizeof(uptr) + 8, count) || count < 0 || (size_t)count > kMaxList) return out;
    out.resize(count);
    if (count && !Rd(mem, out.data(), count * sizeof(uptr))) out.clear();
    return out;
}

// MFC CPtrList: vtable, head, tail, count; node: next, prev, data
std::vector<uptr> PtrList(uptr list) {
    std::vector<uptr> out;
    int count = 0;
    if (!Rd(list + 3 * sizeof(uptr), count) || count < 0 || (size_t)count > kMaxList) return out;
    uptr node = Ptr(list + sizeof(uptr));
    while (node && out.size() < (size_t)count) {
        uptr n[3];
        if (!Rd(node, n, sizeof n)) break;
        out.push_back(n[2]);
        node = n[0];
    }
    return out;
}

// the MSVC RTTI name of an object (".?AVCMapEntity@@" -> "CMapEntity"), empty if it has none
std::string RttiName(uptr obj) {
    uptr vt = Ptr(obj), col = vt ? Ptr(vt - sizeof(uptr)) : 0;
    if (!col) return {};
    uptr td = 0;
#if defined(_WIN64)
    uint32_t tdRva = 0, selfRva = 0;
    if (!Rd(col + 0x0c, tdRva) || !Rd(col + 0x14, selfRva)) return {};
    td = col - selfRva + tdRva;
#else
    td = Ptr(col + 0x0c);
#endif
    std::string n = td ? Str(td + 2 * sizeof(uptr), 128) : "";
    if (n.rfind(".?AV", 0) != 0 || n.size() < 7) return {};
    return n.substr(4, n.size() - 6);
}

// ---------- JSON ----------

std::string Esc(const std::string& s) {
    std::string o;
    for (unsigned char c : s) {
        if (c == '"' || c == '\\') { o += '\\'; o += (char)c; }
        else if (c < 0x20) { char b[8]; snprintf(b, sizeof b, "\\u%04x", c); o += b; }
        else o += (char)c;
    }
    return o;
}

std::string F(double v) {
    if (!std::isfinite(v)) return "null";
    char b[32];
    snprintf(b, sizeof b, "%.6g", v);
    return b;
}

std::string Vec(uptr a) {
    float f[3];
    if (!Rd(a, f, sizeof f)) return "null";
    return "[" + F(f[0]) + "," + F(f[1]) + "," + F(f[2]) + "]";
}

std::string Head(const char* type, const std::string& id) {
    std::string s = std::string("{\"type\":\"") + type + "\"";
    if (!id.empty()) s += ",\"id\":\"" + Esc(id) + "\"";
    return s;
}

std::string Error(const std::string& id, const std::string& message) {
    return Head("error", id) + ",\"message\":\"" + Esc(message) + "\"}";
}

// ---------- the editor ----------

// the active document after checking its vtable, 0 + why
uptr Document(std::string& why) {
    uptr doc = Ptr(Rva("activeDoc"));
    if (!doc) { why = "no map is open in Hammer"; return 0; }
    if (Has("doc.vtable") && Ptr(doc) != Rva("doc.vtable")) { why = "the memory layout does not match this Hammer (document)"; return 0; }
    return doc;
}

std::string Bounds(uptr obj) {
    return ",\"mins\":" + Vec(obj + Num("object.mins")) + ",\"maxs\":" + Vec(obj + Num("object.maxs"));
}

std::string EntityClass(uptr e) { return Str(e + Num("entity.className"), Has("entity.classNameLength") ? Num("entity.classNameLength") : 64); }

// Source: an entity's keyvalues - CUtlDict<MDkeyvalue, unsigned short>, a red-black tree over a node array.
// Walks the tree from its root (every node reached is allocated), each index checked against the array size.
std::string SourceKeyValues(uptr kv) {
    uptr nodes = Ptr(kv + Num("kv.nodes"));
    int alloc = 0;
    uint16_t root = 0xFFFF;
    if (!nodes || !Rd(kv + Num("kv.alloc"), alloc) || alloc <= 0 || !Rd(kv + Num("kv.root"), root)) return "{}";
    size_t size = Num("kv.nodeSize");
    std::string s = "{";
    std::vector<uint16_t> stack{ root };
    std::set<uint16_t> seen;
    while (!stack.empty() && seen.size() < kMaxKeys) {
        uint16_t i = stack.back();
        stack.pop_back();
        if (i == 0xFFFF || i >= alloc || !seen.insert(i).second) continue;
        uptr node = nodes + (uptr)i * size;
        uint16_t left = 0xFFFF, right = 0xFFFF;
        Rd(node + Num("kv.left"), left);
        Rd(node + Num("kv.right"), right);
        if (left == i) continue;   // a free node points to itself
        std::string key = Str(node + Num("kv.key"), Num("kv.keyLength"));
        if (!key.empty()) {
            if (s.size() > 1) s += ",";
            s += "\"" + Esc(key) + "\":\"" + Esc(Str(node + Num("kv.value"), Num("kv.valueLength"))) + "\"";
        }
        stack.push_back(right);
        stack.push_back(left);
    }
    return s + "}";
}

std::string FaceTextures(uptr faces, int n) {
    std::string s;
    if (!faces || n <= 0 || (size_t)n > kMaxFaces) return s;
    std::vector<std::string> textures;
    std::set<std::string> seen;
    for (int i = 0; i < n; ++i) {
        std::string t = Str(faces + (uptr)i * Num("face.size") + Num("face.texture"), Num("face.textureLength") ? Num("face.textureLength") : 16);
        if (seen.insert(t).second) textures.push_back(t);
    }
    s += ",\"faces\":" + std::to_string(n) + ",\"textures\":[";
    for (size_t i = 0; i < textures.size(); ++i) s += (i ? ",\"" : "\"") + Esc(textures[i]) + "\"";
    return s + "]";
}

std::string Item(uptr obj) {
    uptr vt = Ptr(obj);
    if (g_source) {
        if (vt == Rva("entity.vtable"))
            return "{\"kind\":\"entity\",\"className\":\"" + Esc(EntityClass(obj)) + "\",\"origin\":" + Vec(obj + Num("object.origin")) +
                   Bounds(obj) + ",\"keyValues\":" + SourceKeyValues(obj + Num("entity.keyValues")) + "}";
        if (vt == Rva("solid.vtable")) {
            std::string s = "{\"kind\":\"solid\"" + Bounds(obj);
            uptr parent = Ptr(obj + Num("object.parent"));
            if (parent && Ptr(parent) == Rva("entity.vtable")) s += ",\"entity\":\"" + Esc(EntityClass(parent)) + "\"";
            int n = 0;
            Rd(obj + Num("solid.faceCount"), n);
            return s + FaceTextures(Ptr(obj + Num("solid.faces")), n) + "}";
        }
    }
    if (Has("entity.vtable") && vt == Rva("entity.vtable")) {
        std::string s = "{\"kind\":\"entity\",\"className\":\"" + Esc(EntityClass(obj)) + "\",\"origin\":" + Vec(obj + Num("object.origin")) + Bounds(obj);
        uptr data = Ptr(obj + Num("entity.keyValues"));
        int n = 0;
        Rd(obj + Num("entity.keyValueCount"), n);
        s += ",\"keyValues\":{";
        size_t size = Num("entity.keyValueSize"), keyLen = Num("entity.keyLength"), valAt = Num("entity.valueOffset"), valLen = Num("entity.valueLength");
        for (int i = 0; data && i < n && (size_t)i < kMaxKeys; ++i) {
            uptr kv = data + (uptr)i * size;
            if (i) s += ",";
            s += "\"" + Esc(Str(kv, keyLen)) + "\":\"" + Esc(Str(kv + valAt, valLen)) + "\"";
        }
        return s + "}}";
    }
    if (Has("solid.vtable") && vt == Rva("solid.vtable")) {
        std::string s = "{\"kind\":\"solid\"" + Bounds(obj);
        uptr parent = Ptr(obj + Num("object.parent"));
        if (parent && Has("entity.vtable") && Ptr(parent) == Rva("entity.vtable"))
            s += ",\"entity\":\"" + Esc(EntityClass(parent)) + "\"";
        uptr faces = Ptr(obj + Num("solid.faces"));
        int n = 0;
        if (faces) Rd(faces - sizeof(uptr), n);   // new CMapFace[n] keeps n right before the array
        if (n > 0 && (size_t)n <= kMaxFaces) {
            std::vector<std::string> textures;
            std::set<std::string> seen;
            for (int i = 0; i < n; ++i) {
                std::string t = Str(faces + (uptr)i * Num("face.size") + Num("face.texture"), Num("face.textureLength") ? Num("face.textureLength") : 16);
                if (seen.insert(t).second) textures.push_back(t);
            }
            s += ",\"faces\":" + std::to_string(n) + ",\"textures\":[";
            for (size_t i = 0; i < textures.size(); ++i) s += (i ? ",\"" : "\"") + Esc(textures[i]) + "\"";
            s += "]";
        }
        return s + "}";
    }
    std::string kind = RttiName(obj);
    if (kind.rfind("CMap", 0) == 0) kind = kind.substr(4);
    for (auto& c : kind) c = (char)tolower((unsigned char)c);
    return "{\"kind\":\"" + Esc(kind.empty() ? "object" : kind) + "\"" + Bounds(obj) + "}";
}

}  // namespace

bool Load(const std::string& profileLine) {
    auto f = ParseFeatures(profileLine);
    std::lock_guard l(g_lock);
    g_layout = f;
    g_base = 0;
    g_selection = g_camera = false;
    g_reason.clear();
    if (!f.count("activeDoc")) { g_reason = "no memory layout for this build"; return false; }
    auto m = f.find("module");
    g_base = reinterpret_cast<uptr>(m == f.end() ? GetModuleHandleW(nullptr) : GetModuleHandleA(m->second.c_str()));
    g_source = f.count("layout") && f.at("layout") == "source";
    g_selection = true;
    if (g_source)
        for (auto k : kSourceSelectionKeys) g_selection = g_selection && f.count(k);
    else
        for (auto k : kSelectionKeys) g_selection = g_selection && f.count(k);
    for (auto k : kLengths)
        if (Has(k) && (Num(k) == 0 || Num(k) > kMaxLength)) { g_selection = false; g_reason = std::string("bad ") + k + " in the layout"; }
    for (auto k : kStrides)
        if (Has(k) && (Num(k) == 0 || Num(k) > kMaxStride)) { g_selection = false; g_reason = std::string("bad ") + k + " in the layout"; }
    if (!g_selection && g_reason.empty()) g_reason = "the layout has no selection fields";
    g_camera = f.count("camera.matrix") || f.count("camera.forward");
    for (auto k : kCameraKeys) g_camera = g_camera && f.count(k);
    return g_base != 0;
}

std::string Reason() {
    std::lock_guard l(g_lock);
    return g_reason;
}

bool Ready() {
    std::lock_guard l(g_lock);
    return g_base != 0 && g_selection;
}

std::string Selection(const std::string& id) {
    std::lock_guard l(g_lock);
    if (!g_base || !g_selection) return Error(id, "live reading is not available for this Hammer build" + (g_reason.empty() ? std::string() : ": " + g_reason));
    std::string why;
    uptr doc = Document(why);
    if (!doc) return Error(id, why);
    // GoldSrc: a CPtrList inside the document; Source: a pointer to CSelection, whose CUtlVector lists the objects
    auto objs = g_source ? UtlVector(Ptr(doc + Num("doc.selection")) + Num("selection.list")) : PtrList(doc + Num("doc.selection"));
    std::string s = Head("selection", id) + ",\"count\":" + std::to_string(objs.size());
    if (Has("doc.selectionBounds") && !objs.empty()) {
        uptr b = (g_source ? Ptr(doc + Num("doc.selection")) : doc) + Num("doc.selectionBounds");
        s += ",\"bounds\":{\"mins\":" + Vec(b) + ",\"maxs\":" + Vec(b + 12) + "}";
    }
    s += ",\"items\":[";
    for (size_t i = 0; i < objs.size() && i < kMaxItems; ++i) s += (i ? "," : "") + Item(objs[i]);
    s += "]";
    if (objs.size() > kMaxItems) s += ",\"truncated\":true";
    return s + "}";
}

std::string Camera(const std::string& id) {
    std::lock_guard l(g_lock);
    if (!g_base || !g_camera) return Error(id, "live reading is not available for this Hammer build");
    std::string why;
    uptr doc = Document(why);
    if (!doc) return Error(id, why);
    for (uptr view : PtrList(doc + Num("doc.views"))) {
        if (Ptr(view) != Rva("view3d.vtable")) continue;
        uptr cam = Ptr(view + Num("view3d.camera"));
        if (cam && Has("camera.indirect")) cam = Ptr(cam);  // Hammer++: the view points to a holder whose first field is the camera
        float fwd[3];
        if (Has("camera.matrix")) {
            // view matrix: the eye looks along -z, so forward = -(third row of the rotation); OpenGL modelview is
            // column-major (GoldSrc), a Source VMatrix is row-major ("camera.matrixRows")
            float m[16];
            if (!cam || !Rd(cam + Num("camera.matrix"), m, sizeof m)) return Error(id, "the 3D view has no camera");
            if (Has("camera.matrixRows")) { fwd[0] = -m[8]; fwd[1] = -m[9]; fwd[2] = -m[10]; }
            else { fwd[0] = -m[2]; fwd[1] = -m[6]; fwd[2] = -m[10]; }
        } else if (!cam || !Rd(cam + Num("camera.forward"), fwd, sizeof fwd)) {
            return Error(id, "the 3D view has no camera");
        }
        const double deg = 57.29577951308232;
        std::string s = Head("camera", id) + ",\"position\":" + Vec(cam + Num("camera.position")) +
                        ",\"forward\":[" + F(fwd[0]) + "," + F(fwd[1]) + "," + F(fwd[2]) + "]" +
                        ",\"yaw\":" + F(std::atan2(fwd[1], fwd[0]) * deg) +
                        ",\"pitch\":" + F(-std::asin(std::fmax(-1.0, std::fmin(1.0, (double)fwd[2]))) * deg);
        float fov = 0;
        if (Has("camera.fov") && Rd(cam + Num("camera.fov"), fov)) s += ",\"fov\":" + F(fov);
        return s + "}";
    }
    return Error(id, "no 3D view is open");
}

}  // namespace live
