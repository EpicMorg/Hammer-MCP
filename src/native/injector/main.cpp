// hmcp-injector32/64.exe --pid N --adapter <dir with hmcp-adapter-*.dll>
// Started by the Hammer-MCP server only after the user pressed "Connect adapter" (InjectPolicy in C# decides first);
// checks the same policy again here, before OpenProcess for writing.
// Exit codes: 0 injected, 2 usage, 3 refused by the policy, 4 failed, 5 an adapter is already running in that process.
#include "hmcp.h"
#include <tlhelp32.h>
#include <cstdio>

// is any Hammer-MCP adapter module loaded in the process? (same bitness as this injector)
static bool AdapterLoaded(DWORD pid) {
    HANDLE snap = CreateToolhelp32Snapshot(TH32CS_SNAPMODULE | TH32CS_SNAPMODULE32, pid);
    if (snap == INVALID_HANDLE_VALUE) return false;
    MODULEENTRY32W m{ sizeof m };
    bool found = false;
    for (BOOL ok = Module32FirstW(snap, &m); ok && !found; ok = Module32NextW(snap, &m))
        found = _wcsnicmp(m.szModule, L"hmcp-adapter-", 13) == 0;
    CloseHandle(snap);
    return found;
}

// the adapter's pipe exists (free, or busy with a connected server)
static bool PipeExists(DWORD pid) {
    std::wstring name = hmcp::PipeName(pid);
    return WaitNamedPipeW(name.c_str(), 1) || GetLastError() == ERROR_SEM_TIMEOUT;
}

static int Fail(int code, const char* fmt, ...) {
    va_list a; va_start(a, fmt); vfprintf(stderr, fmt, a); va_end(a); fputc('\n', stderr); return code;
}

int wmain(int argc, wchar_t** argv) {
    DWORD pid = 0; std::wstring adapterDir;
    for (int i = 1; i < argc; ++i) {
        if (!wcscmp(argv[i], L"--pid") && i + 1 < argc) pid = wcstoul(argv[++i], nullptr, 10);
        else if (!wcscmp(argv[i], L"--adapter") && i + 1 < argc) adapterDir = argv[++i];
    }
    if (!pid || adapterDir.empty()) return Fail(2, "usage: hmcp-injector --pid N --adapter <dir>");

    // 1) read-only look at the target: exe path and bitness
    HANDLE q = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, FALSE, pid);
    if (!q) return Fail(4, "cannot query process %lu (%lu)", pid, GetLastError());
    wchar_t exe[MAX_PATH * 2]; DWORD len = (DWORD)(sizeof exe / sizeof *exe);
    BOOL okName = QueryFullProcessImageNameW(q, 0, exe, &len);
    USHORT processMachine = 0, nativeMachine = 0;
    IsWow64Process2(q, &processMachine, &nativeMachine);
    CloseHandle(q);
    if (!okName) return Fail(4, "cannot read the exe path of %lu", pid);
    bool targetIs64 = processMachine == IMAGE_FILE_MACHINE_UNKNOWN && nativeMachine != IMAGE_FILE_MACHINE_I386;
    if (targetIs64 != (sizeof(void*) == 8))
        return Fail(3, "refused: use hmcp-injector%d for a %d-bit Hammer", targetIs64 ? 64 : 32, targetIs64 ? 64 : 32);

    // 2) policy again (the C# server already checked): known build with an adapter, never a game / Source 2
    auto policy = hmcp::CheckPolicy(exe, targetIs64);
    if (!policy.build) return Fail(3, "refused: %s", policy.reason.c_str());
    // an adapter already there: running (pipe up) -> nothing to do; unloading (no pipe yet) -> wait for it to go.
    // Loading the same DLL while the old copy is still unloading would only bump its refcount: no new worker, and the
    // old worker's FreeLibrary would then leave a module without a thread that nothing can unload.
    if (AdapterLoaded(pid)) {
        if (PipeExists(pid)) { printf("already injected: an adapter is running in %lu\n", pid); return 5; }
        for (int i = 0; i < 100 && AdapterLoaded(pid); ++i) Sleep(100);
        if (AdapterLoaded(pid))
            return Fail(4, "an adapter is still loaded in %lu but not answering (unloading or stuck); try again, or restart Hammer", pid);
    }
    std::wstring dll = adapterDir + L"\\hmcp-adapter-" + hmcp::Widen(policy.build->adapter) + L".dll";
    if (!hmcp::Exists(dll)) return Fail(4, "adapter not found: %ls", dll.c_str());

    // 3) LoadLibraryW in the target
    HANDLE p = OpenProcess(PROCESS_CREATE_THREAD | PROCESS_QUERY_INFORMATION | PROCESS_VM_OPERATION | PROCESS_VM_WRITE | PROCESS_VM_READ, FALSE, pid);
    if (!p) return Fail(4, "cannot open process %lu for injection (%lu)", pid, GetLastError());
    SIZE_T bytes = (dll.size() + 1) * sizeof(wchar_t);
    void* remote = VirtualAllocEx(p, nullptr, bytes, MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE);
    int rc = 4;
    if (remote && WriteProcessMemory(p, remote, dll.c_str(), bytes, nullptr)) {
        auto loadLibrary = (LPTHREAD_START_ROUTINE)GetProcAddress(GetModuleHandleW(L"kernel32.dll"), "LoadLibraryW");
        HANDLE t = CreateRemoteThread(p, nullptr, 0, loadLibrary, remote, 0, nullptr);
        if (t) {
            WaitForSingleObject(t, 15000);
            DWORD loaded = 0; GetExitCodeThread(t, &loaded);
            CloseHandle(t);
            if (loaded) { printf("injected %s into %lu (%s)\n", policy.build->adapter, pid, policy.build->id); rc = 0; }
            else Fail(4, "LoadLibraryW failed in the target");
        } else Fail(4, "CreateRemoteThread failed (%lu)", GetLastError());
    } else Fail(4, "cannot write the DLL path into the target (%lu)", GetLastError());
    if (remote) VirtualFreeEx(p, remote, 0, MEM_RELEASE);
    CloseHandle(p);
    return rc;
}
