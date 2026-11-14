// Shared helpers of the injector and the adapters (header-only, Win32 only).
#pragma once
#include <windows.h>
#include <bcrypt.h>
#include <string>
#include <vector>
#include "allowed_builds.h"

namespace hmcp {

// \\.\pipe\hammer-mcp-adapter-<pid>: one adapter per Hammer process; the C# server connects as the client.
inline std::wstring PipeName(DWORD pid) { return L"\\\\.\\pipe\\hammer-mcp-adapter-" + std::to_wstring(pid); }

// lower-case hex sha256 of a file, empty on error
inline std::string Sha256File(const std::wstring& path) {
    HANDLE f = CreateFileW(path.c_str(), GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_DELETE, nullptr, OPEN_EXISTING, 0, nullptr);
    if (f == INVALID_HANDLE_VALUE) return {};
    BCRYPT_ALG_HANDLE alg = nullptr; BCRYPT_HASH_HANDLE hash = nullptr; std::string hex;
    if (BCryptOpenAlgorithmProvider(&alg, BCRYPT_SHA256_ALGORITHM, nullptr, 0) == 0 &&
        BCryptCreateHash(alg, &hash, nullptr, 0, nullptr, 0, 0) == 0) {
        std::vector<unsigned char> buf(1 << 16); DWORD n = 0; bool ok = true;
        while (ReadFile(f, buf.data(), (DWORD)buf.size(), &n, nullptr) && n > 0)
            if (BCryptHashData(hash, buf.data(), n, 0) != 0) { ok = false; break; }
        unsigned char digest[32];
        if (ok && BCryptFinishHash(hash, digest, sizeof digest, 0) == 0) {
            static const char* x = "0123456789abcdef";
            for (unsigned char b : digest) { hex += x[b >> 4]; hex += x[b & 15]; }
        }
    }
    if (hash) BCryptDestroyHash(hash);
    if (alg) BCryptCloseAlgorithmProvider(alg, 0);
    CloseHandle(f);
    return hex;
}

inline std::wstring Dir(const std::wstring& path) { auto p = path.find_last_of(L"\\/"); return p == std::wstring::npos ? L"." : path.substr(0, p); }
inline std::wstring FileName(const std::wstring& path) { auto p = path.find_last_of(L"\\/"); return p == std::wstring::npos ? path : path.substr(p + 1); }
inline std::wstring Widen(const char* s) { return std::wstring(s, s + strlen(s)); }
inline bool Exists(const std::wstring& p) { return GetFileAttributesW(p.c_str()) != INVALID_FILE_ATTRIBUTES; }
inline bool IEquals(const std::wstring& a, const std::wstring& b) { return CompareStringOrdinal(a.c_str(), -1, b.c_str(), -1, TRUE) == CSTR_EQUAL; }

// The editor DLL that belongs to THIS exe. Several editors can share one bin folder (Bestiarium: Slammin's hammer.exe +
// hammer_dll.dll and Hammer++'s hammerplusplus.exe + hammerplusplus\bin\hammerplusplus_dll.dll), so the DLL is chosen
// by the exe name, never by whatever lies around: hammerplusplus*.exe -> hammerplusplus_dll.dll (next to it or in
// hammerplusplus\bin), any other Hammer exe -> hammer_dll.dll / hammer.dll next to it. Empty if this exe has none of dllName.
inline std::wstring EditorDll(const std::wstring& exe, const char* dllName) {
    std::wstring dir = Dir(exe), name = Widen(dllName), exeName = FileName(exe);
    bool hpp = _wcsnicmp(exeName.c_str(), L"hammerplusplus", 14) == 0;
    bool hppDll = IEquals(name, L"hammerplusplus_dll.dll");
    if (hpp != hppDll) return {};
    std::vector<std::wstring> candidates{ dir + L"\\" + name };
    if (hpp) candidates.push_back(dir + L"\\hammerplusplus\\bin\\" + name);
    for (auto& c : candidates)
        if (Exists(c)) return c;
    return {};
}

inline bool EndsWithExe(const char* s) { size_t n = strlen(s); return n > 4 && _stricmp(s + n - 4, ".exe") == 0; }

// Same rules as HammerMcp.Core.Registry.InjectPolicy: only a standalone editor of a build that has an adapter;
// never a game process (VAC), never Source 2 (tools\hammer.dll lives inside the game), never an unknown build.
// Source 1: hammer.exe / hammerplusplus.exe + the editor DLL's sha256. GoldSrc: the editor is the exe itself
// (registry match.dll = hammer.exe / wc.exe / neon.exe ...): the exe's own sha256 must match, under any file name.
struct PolicyResult { const HmcpAllowedBuild* build; std::string reason; std::string sha256; };
inline PolicyResult CheckPolicy(const std::wstring& exe, bool targetIs64) {
    std::wstring name = FileName(exe);
    if (Exists(Dir(exe) + L"\\tools\\hammer.dll"))
        return { nullptr, "Source 2 Hammer runs inside the game process (VAC / anti-cheat): never injected", {} };
    bool standaloneHammer = IEquals(name, L"hammer.exe") || IEquals(name, L"hammerplusplus.exe");
    // ~180 allowed builds but only a few editor files: hash each file once
    std::vector<std::pair<std::wstring, std::string>> hashed;
    auto hash = [&](const std::wstring& path) {
        for (auto& [p, h] : hashed) if (p == path) return h;
        hashed.emplace_back(path, Sha256File(path));
        return hashed.back().second;
    };
    bool sawExeEditor = false;
    for (const HmcpAllowedBuild* b = kHmcpAllowedBuilds; b->id; ++b) {
        if (b->x64 != targetIs64) continue;
        std::wstring file;
        if (EndsWithExe(b->editorDll)) {           // GoldSrc: the exe is the editor; its sha256 identifies it whatever
            file = exe;                            // it is called (hammer34.exe = hammer.exe 3.4); a game exe never matches
            sawExeEditor = true;
        } else {
            if (!standaloneHammer) continue;
            file = EditorDll(exe, b->editorDll);
            if (file.empty()) continue;
        }
        std::string sha = hash(file);
        if (sha == b->sha256) return { b, "allowed: " + std::string(b->id), sha };
    }
    if (!standaloneHammer)
        return { nullptr, "not a standalone Hammer executable: game and other processes are never injected", {} };
    return { nullptr, "unknown Hammer build or no adapter for it: never injected", {} };
}

// "Local\hammer-mcp-adapter-<pid>": held by the adapter's worker for its whole life (one adapter per process)
inline std::wstring InstanceName(DWORD pid) { return L"Local\\hammer-mcp-adapter-" + std::to_wstring(pid); }

} // namespace hmcp
