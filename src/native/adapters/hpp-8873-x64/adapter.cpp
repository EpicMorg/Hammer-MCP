// Adapter for Hammer++ 8873 x64 (registry id hpp-tf2-8873-x64). Layer 0: pipe, hello, ping, unload.
// Protocol: one JSON object per line over \\.\pipe\hammer-mcp-adapter-<pid> (local clients only); the adapter speaks first.
//   adapter -> {"type":"hello","adapter":"hpp-8873-x64","version":"0.1.0","pid":N,"build":"hpp-tf2-8873-x64","editorSha256":"..."}
//   client  -> {"cmd":"ping"}   adapter -> {"type":"pong"}
//   client  -> {"cmd":"unload"} adapter -> {"type":"bye"}, then FreeLibraryAndExitThread (re-inject without restarting Hammer)
#include "hmcp.h"
#include <string>

namespace {

constexpr const char* kAdapterId = "hpp-8873-x64";
HMODULE g_self = nullptr;

bool WriteLine(HANDLE pipe, const std::string& line) {
    std::string s = line + "\n"; DWORD n = 0;
    return WriteFile(pipe, s.data(), (DWORD)s.size(), &n, nullptr) && n == s.size();
}

std::string Hello() {
    wchar_t exe[MAX_PATH * 2]; GetModuleFileNameW(nullptr, exe, MAX_PATH * 2);
    std::string build = "unknown", sha;
    for (const HmcpAllowedBuild* b = kHmcpAllowedBuilds; b->id; ++b)
        if (std::string(b->adapter) == kAdapterId) {
            build = b->id;
            sha = hmcp::Sha256File(hmcp::EditorDll(exe, b->editorDll));
        }
    return std::string("{\"type\":\"hello\",\"adapter\":\"") + kAdapterId + "\",\"version\":\"" HMCP_NATIVE_VERSION "\",\"pid\":" +
           std::to_string(GetCurrentProcessId()) + ",\"build\":\"" + build + "\",\"editorSha256\":\"" + sha + "\"}";
}

// serves one client at a time until it disconnects; true = unload requested
bool Serve(HANDLE pipe) {
    if (!WriteLine(pipe, Hello())) return false;
    std::string buf; char chunk[512]; DWORD n = 0;
    while (ReadFile(pipe, chunk, sizeof chunk, &n, nullptr) && n > 0) {
        buf.append(chunk, n);
        for (size_t eol; (eol = buf.find('\n')) != std::string::npos; buf.erase(0, eol + 1)) {
            std::string msg = buf.substr(0, eol);
            if (msg.find("\"ping\"") != std::string::npos) WriteLine(pipe, "{\"type\":\"pong\"}");
            else if (msg.find("\"unload\"") != std::string::npos) { WriteLine(pipe, "{\"type\":\"bye\"}"); return true; }
            else WriteLine(pipe, "{\"type\":\"error\",\"message\":\"unknown command\"}");
        }
    }
    return false;
}

DWORD WINAPI Worker(LPVOID) {
    std::wstring name = hmcp::PipeName(GetCurrentProcessId());
    for (;;) {
        HANDLE pipe = CreateNamedPipeW(name.c_str(), PIPE_ACCESS_DUPLEX,
            PIPE_TYPE_BYTE | PIPE_READMODE_BYTE | PIPE_WAIT | PIPE_REJECT_REMOTE_CLIENTS, 1, 4096, 4096, 0, nullptr);
        if (pipe == INVALID_HANDLE_VALUE) break;
        bool connected = ConnectNamedPipe(pipe, nullptr) || GetLastError() == ERROR_PIPE_CONNECTED;
        bool unload = connected && Serve(pipe);
        FlushFileBuffers(pipe);
        DisconnectNamedPipe(pipe);
        CloseHandle(pipe);
        if (unload) break;
    }
    FreeLibraryAndExitThread(g_self, 0);
}

} // namespace

BOOL WINAPI DllMain(HINSTANCE self, DWORD reason, LPVOID) {
    if (reason == DLL_PROCESS_ATTACH) {
        g_self = self;
        DisableThreadLibraryCalls(self);
        // nothing heavy under the loader lock: the worker thread does the rest
        if (HANDLE t = CreateThread(nullptr, 0, Worker, nullptr, 0, nullptr)) CloseHandle(t);
    }
    return TRUE;
}
