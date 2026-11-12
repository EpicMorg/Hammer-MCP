# Third-party notices

Hammer-MCP is MIT-licensed (see [LICENSE](LICENSE)). It is built with and redistributes the components below.

## Shipped in `hammer-mcp.exe`

| Component | Version | License | Project |
|---|---|---|---|
| .NET runtime, ASP.NET Core, Windows Forms | 10 | MIT | https://github.com/dotnet/runtime · https://github.com/dotnet/aspnetcore · https://github.com/dotnet/winforms |
| Model Context Protocol C# SDK (`ModelContextProtocol`, `.Core`, `.AspNetCore`) | 2.4.0 | Apache-2.0 | https://github.com/modelcontextprotocol/csharp-sdk |
| Microsoft.Extensions.AI.Abstractions and other `Microsoft.Extensions.*` packages | 10.x | MIT | https://github.com/dotnet/extensions |
| Sledge.Formats, Sledge.Formats.Map, Sledge.Formats.GameData (`.rmf` / `.map` / `.jmf`; FGD as an alternative parser) | 1.2.9 / 1.1.8 | MIT | https://github.com/LogicAndTrick/sledge-formats |
| ValveKeyValue (`.vmt`, `GameConfig.txt`, KeyValues) | 0.80 | MIT | https://github.com/ValveResourceFormat/ValveKeyValue |

## Build and tests only (not shipped)

| Component | Version | License | Project |
|---|---|---|---|
| xUnit.net v3 | 4.0.3 | Apache-2.0 | https://github.com/xunit/xunit |
| Microsoft.NET.Test.Sdk, Microsoft.Testing.Platform | 18.x / 2.x | MIT | https://github.com/microsoft/vstest · https://github.com/microsoft/testfx |

## Planned (not used yet)

| Component | License | Use |
|---|---|---|
| MinHook or Microsoft Detours | BSD-2-Clause / MIT | expert-mode inject adapters (C++) |
| Zydis | MIT | signature scanning in adapters (C++) |

## Protocols and prior art (no code used)

- Hammer shell channel (`Worldcraft_ShellMessageWnd`, `WM_COPYDATA` text commands) as published by Valve in the
  Source SDK 2013: `src/public/editor_sendcommand.{h,cpp}`, `src/game/server/wcedit.cpp`.
- Hammer++ by ficool2 — the shell channel is present since build 8867.
- Menu command ids were read from the resources of shipped editor binaries.

Hammer-MCP contains no Valve source code. Valve, Hammer and Source are trademarks of Valve Corporation.
