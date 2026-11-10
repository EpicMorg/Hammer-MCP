# Hammer-MCP

MCP server (proof of concept) that connects AI agents to Valve Hammer Editor.

The server runs as a tray app on the Windows PC with Hammer and speaks MCP over HTTP.
In the **basic mode** it drives a running Hammer through its built-in shell channel
(`Worldcraft_ShellMessageWnd` + `WM_COPYDATA`, the mechanism from the Source SDK `editor_sendcommand.cpp`)
and reads the map from the saved `.vmf`. An **expert mode** with per-build inject adapters is planned.

Status: early PoC. Verified live on Hammer++ 8873 x64: create / keyvalue / rotate / delete entities, undo works.

## Layout

```
src/HammerMcp.Core     map model, .vmf versioninfo, Hammer build registry
src/HammerMcp.Shell    shell channel (WM_COPYDATA), sessions, Hammer process / open map detection
src/HammerMcp.Server   MCP server (Streamable HTTP) and tools
src/HammerMcp.App      tray app (WinForms) — hammer-mcp.exe
tests/HammerMcp.Tests  unit tests
registry/hammers.json  known Hammer builds
native/                C++ injector and adapters (planned)
```

## Build

.NET 10 SDK. `dotnet build HammerMcp.sln`, `dotnet test --solution HammerMcp.sln` (Microsoft.Testing.Platform, see `global.json`). The app and server target `net10.0-windows`
(they build on Linux too thanks to `EnableWindowsTargeting`, but run on Windows 10/11 x64 only).

## Use

Run `hammer-mcp.exe`, tray menu → Start. The server listens on `http://127.0.0.1:47800/mcp`.
Claude Code: `claude mcp add --transport http hammer http://127.0.0.1:47800/mcp`.
Open a map in Hammer and **save it** — sessions use the `mapversion` from the saved `.vmf`.

Tools: `hammer_status`, `create_entity`, `set_keyvalue`, `rotate_entity`, `delete_entity`.

## License

MIT, see `LICENSE`.
