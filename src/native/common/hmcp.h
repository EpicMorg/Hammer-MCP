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

// The editor DLL next to a Hammer exe: Hammer++ keeps it in bin\hammerplusplus\bin, stock Hammer next to the exe.
inline std::wstring EditorDll(const std::wstring& exe, const char* dllName) {
    std::wstring dir = Dir(exe), name = Widen(dllName);
    for (auto c : { dir + L"\\hammerplusplus\\bin\\" + name, dir + L"\\" + name })
        if (Exists(c)) return c;
    return {};
}

// Same rules as HammerMcp.Core.Registry.InjectPolicy: only a standalone Hammer exe of a build that has an adapter;
// never a game process (VAC), never Source 2 (tools\hammer.dll lives inside the game), never an unknown build.
struct PolicyResult { const HmcpAllowedBuild* build; std::string reason; std::string sha256; };
inline PolicyResult CheckPolicy(const std::wstring& exe, bool targetIs64) {
    std::wstring name = FileName(exe);
    if (Exists(Dir(exe) + L"\\tools\\hammer.dll"))
        return { nullptr, "Source 2 Hammer runs inside the game process (VAC / anti-cheat): never injected", {} };
    if (!IEquals(name, L"hammer.exe") && !IEquals(name, L"hammerplusplus.exe"))
        return { nullptr, "not a standalone Hammer executable: game and other processes are never injected", {} };
    for (const HmcpAllowedBuild* b = kHmcpAllowedBuilds; b->id; ++b) {
        if (b->x64 != targetIs64) continue;
        std::wstring dll = EditorDll(exe, b->editorDll);
        if (dll.empty()) continue;
        std::string sha = Sha256File(dll);
        if (sha == b->sha256) return { b, "allowed: " + std::string(b->id), sha };
    }
    return { nullptr, "unknown Hammer build or no adapter for it: never injected", {} };
}

} // namespace hmcp
