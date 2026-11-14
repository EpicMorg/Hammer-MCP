# native — injector and adapters (Advanced mode, work in progress on `feature/inject`)

Clean-room work on shipped binaries only: no Valve source code, no Source SDK headers, no leaked code.

- `injector/` — `hmcp-injector32.exe` / `hmcp-injector64.exe --pid N --adapter <dir>`. Started by the Hammer-MCP server
  only after the user pressed **Connect adapter**; the C# `InjectPolicy` decides first, the injector checks again before
  opening the process for writing: standalone `hammer.exe` / `hammerplusplus.exe` only, never a game process (VAC),
  never Source 2, only builds whose editor-DLL sha256 has an `injectAdapter` in `src/registry/hammers.json`
  (the list is generated from the registry at build time — `tools/gen_allowed_builds.py`).
- `adapters/<adapter-id>/` — one DLL per Hammer build, e.g. `hmcp-adapter-hpp-8873-x64.dll` for Hammer++ 8873 x64.
  Layer 0: named pipe `\\.\pipe\hammer-mcp-adapter-<pid>` (local clients only), one JSON object per line:
  hello (adapter, version, pid, build, editorSha256) → `{"cmd":"ping"}` / `{"cmd":"unload"}` (unloads itself, so it can be
  re-injected without restarting Hammer). Next: the MCP menu inside Hammer, then live map reading.
- Static CRT (`/MT`), icon and version resources in every binary.

Build:

```bash
src/native/build.sh                                   # Linux: clang-cl + lld-link + xwin (CRT/SDK in /opt/xwin)
cmake -S src/native -B build/native-x64 -A x64        # Windows: MSVC (reference build, also in CI)
cmake --build build/native-x64 --config Release
```
