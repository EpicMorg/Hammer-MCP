# native

C++ part of Hammer-MCP (planned, not started):

- `HammerMcp.Injector` — tiny injector, built for x86 and x64 (a 64-bit host cannot simply load a DLL into a 32-bit Hammer).
- `adapters/<build-id>` — expert-mode DLLs injected into a specific Hammer build (ids match `registry/hammers.json`):
  live map read, selection, camera, events. Hooks: MinHook or Detours; signatures: Zydis.

Clean-room reverse engineering of shipped binaries only. No leaked Valve source code in this repository.
