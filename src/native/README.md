# native — injector and adapters (Advanced mode, work in progress on `feature/inject`)

Clean-room work on shipped binaries only: no Valve source code, no Source SDK headers, no leaked code.

- `injector/` — `hmcp-injector32.exe` / `hmcp-injector64.exe --pid N --adapter <dir>`. Started by the Hammer-MCP server
  only after the user pressed **Connect adapter**; the C# `InjectPolicy` decides first, the injector checks again before
  opening the process for writing: standalone `hammer.exe` / `hammerplusplus.exe` only, never a game process (VAC),
  never Source 2, only builds whose editor-DLL sha256 has an `injectAdapter` in `src/registry/hammers.json`
  (the list is generated from the registry at build time — `tools/gen_allowed_builds.py`).
- `adapters/base/` — the adapter: **one DLL per bitness**, `hmcp-adapter-x86.dll` / `hmcp-adapter-x64.dll`, for every
  registry build with an `injectAdapter` support level (stock Valve, Hammer++, Slammin', Black Mesa, Psycho-A, GoldSrc
  Worldcraft / Hammer 3.x and mods: 176 builds, level `menu` = layers 0-1). It finds out itself which build it runs in
  (sha256 of the editor file) and says so in hello. Build-specific knowledge for later layers (signatures, offsets) will
  be data per build, not separate DLLs. Never injected: Source 2 (game process), Strata and J.A.C.K. (Qt).
- Injector exit code 5: an adapter is already running in that process (its pipe answers); if one is still unloading,
  the injector waits up to 10 s instead of loading the same module again. The adapter also holds a named mutex
  `Local\\hammer-mcp-adapter-<pid>`, so a copy from another folder cannot start a second menu.
- The pipe is up right after injection; the main window is looked for in the background (up to 10 minutes - Hammer may
  be busy loading a map), so a client should wait for the pipe a few seconds, not for the menu.
  Layer 0: named pipe `\\.\pipe\hammer-mcp-adapter-<pid>` (local clients only), one JSON object per line:
  hello (adapter, version, pid, build, editorSha256) → `{"cmd":"ping"}` / `{"cmd":"unload"}` (unloads itself, so it can be
  re-injected without restarting Hammer). Next: the MCP menu inside Hammer, then live map reading.
- Static CRT (`/MT`), icon and version resources in every binary.

Build:

```bash
src/native/build.sh                                   # Linux: clang-cl + lld-link + xwin (CRT/SDK in /opt/xwin)
cmake -S src/native -B build/native-x64 -A x64        # Windows: MSVC from VS 2022+ (VS 2019 Build Tools without the CMake component do not work); also in CI
cmake --build build/native-x64 --config Release
```
