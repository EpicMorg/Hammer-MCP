# Hammer-MCP

MCP server (proof of concept) that connects AI agents to Valve Hammer Editor.

The server runs as a tray app on the Windows PC with Hammer and speaks MCP over HTTP.
In the **basic mode** it drives a running Hammer through its built-in shell channel
(`Worldcraft_ShellMessageWnd` + `WM_COPYDATA`, the mechanism from the Source SDK `editor_sendcommand.cpp`)
and reads the map from the saved `.vmf`. An **expert mode** with per-build inject adapters is planned.

Status: early PoC. Verified live on Hammer++ 8873 x64: create / keyvalue / rotate / delete entities, undo works.

## Layout

```
src/HammerMcp.sln            solution (+ Directory.Build.props, global.json)
src/src/HammerMcp.Core       map model, .vmf versioninfo, Hammer build registry, menu command ids, PE header
src/src/HammerMcp.Shell      shell channel (WM_COPYDATA), menu commands (WM_COMMAND), sessions, Hammer process discovery
src/src/HammerMcp.Server     MCP server (Streamable HTTP, optional Bearer token) and tools
src/src/HammerMcp.App        hammer-mcp.exe: WinForms window + tray (process attach, meta info, settings, logs)
src/tests/HammerMcp.Tests    unit tests
src/registry/hammers.json    known Hammer builds
src/native/                  C++ injector and adapters (planned, CMake)
docs/media/                  icons and artwork
```

## Build

.NET 10 SDK, from `src/`: `dotnet build HammerMcp.sln`, `dotnet test --solution HammerMcp.sln`
(Microsoft.Testing.Platform, see `global.json`). App and server target `net10.0-windows`: they build on Linux too
(`EnableWindowsTargeting`), but run on Windows 10/11 x64 only.

## Use

1. Run `hammer-mcp.exe`. Settings tab: mode, address (localhost / all / a LAN address), port, token → Apply.
   A token is required for anything but localhost.
2. Main tab: pick the Hammer process → Attach (meta info shows exe, arch, known build, shell channel, open map, mapversion).
   Without attaching, the server uses the first Hammer that has a shell channel.
3. Start. Tray menu → "Copy client config" gives e.g.
   `claude mcp add --transport http hammer http://127.0.0.1:47800/mcp --header "Authorization: Bearer <token>"`.
4. Open a map in Hammer and **save it** — sessions use the `mapversion` from the saved `.vmf`.

Modes: Auto (live edits where a shell channel exists), Basic (read-only), Shell (live edits + menu commands),
Advanced (injected adapter — not implemented yet, works as Shell).

Tools: `hammer_status`; live edits `create_entity`, `set_keyvalue`, `rotate_entity`, `delete_entity`;
menu `save_map`, `undo`, `redo`, `open_run_map_dialog`, `open_check_for_problems`.
Closing the window hides it to the tray; File > Exit or tray > Exit quits.

## License

MIT, see `LICENSE`.
