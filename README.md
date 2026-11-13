<p align="center">
  <img src="docs/media/hammer-mcp-red-green-v2.png" alt="Hammer-MCP" width="200">
</p>

<h1 align="center">Hammer-MCP</h1>

<p align="center">
  <b>Let AI agents see and edit maps in Valve Hammer Editor — live.</b><br>
  An <a href="https://modelcontextprotocol.io">MCP</a> server for Hammer (Source 1), <a href="https://github.com/ficool2/HammerPlusPlus-Website">Hammer++</a> and friends.
</p>

<p align="center">
  <img alt="status" src="https://img.shields.io/badge/status-proof%20of%20concept-orange">
  <img alt="platform" src="https://img.shields.io/badge/platform-Windows%2010%2F11%20x64-0078D6">
  <img alt=".NET" src="https://img.shields.io/badge/.NET-10-512BD4">
  <img alt="MCP" src="https://img.shields.io/badge/MCP-Streamable%20HTTP-6f42c1">
  <img alt="license" src="https://img.shields.io/badge/license-MIT-green">
</p>

---

Ask your agent *"put a warm light at each corner of the room and name them by the scheme"* — and watch the entities
appear in the Hammer you already have open. No plugins, no patched editor: Hammer-MCP talks to Hammer through the
**shell channel** that Valve built into the editor 20 years ago (`Worldcraft_ShellMessageWnd`, the mechanism behind
[`editor_sendcommand.cpp`](https://github.com/ValveSoftware/source-sdk-2013/blob/master/src/public/editor_sendcommand.cpp) in the Source SDK) and through Hammer's own menu commands.

```
 AI agent (Claude Code, Cursor, …)
        │  MCP over HTTP (+ optional Bearer token, LAN-capable)
        ▼
 ┌────────────────────────────┐      WM_COPYDATA  (shell channel: entities, keyvalues, nodes)
 │  hammer-mcp.exe  (tray)    │ ───► WM_COMMAND   (menu: save, undo, redo, run map, …)        ──►  Hammer / Hammer++
 │  process attach · settings │ ───► reads the saved .vmf (map summary, mapversion)
 │  live log                  │
 └────────────────────────────┘
```

## What works today

Verified live on **[Hammer++](https://github.com/ficool2/HammerPlusPlus-Website) 8873 x64** ([TF2 Classified](https://store.steampowered.com/app/3545060/)), from an agent on another machine over the LAN:

| | |
|---|---|
| ✅ Create point entities with keyvalues (`light`, `info_target`, `prop_dynamic` with a model, …) | ✅ Rotate entities (a spinning locker at ~28 edits/s, Hammer stays responsive) |
| ✅ Set keyvalues, delete entities | ✅ Edits land in Hammer's **undo** history — Ctrl+Z works |
| ✅ Save / undo / redo / Run Map / Check for Problems via menu commands | ✅ Map summary from the saved `.vmf` (entities, brushes, classes, skybox…) |
| ✅ Recognises the running Hammer build by hash (registry of known builds) | ✅ Token auth, localhost by default |

## Supported editors

The shell channel exists in **every stock Source Hammer since 2006** and in **Hammer++ since build 8867**;
menu command ids are identical across **all 152 Source Hammer builds** we analysed.

| Editor | Live edits (shell) | Menu commands | Map files |
|---|:---:|:---:|:---:|
| Stock Hammer — Source SDK 2013 SP/MP, HL2, Portal, TF2, CS:S, DoD:S, L4D/L4D2, Portal 2, GMod, … (x86 & x64) | ✅ | ✅ | ✅ |
| [Hammer++](https://github.com/ficool2/HammerPlusPlus-Website) 8867+ (all games) | ✅ | ✅ | ✅ |
| [Slammin' Hammer 5.2](https://developer.valvesoftware.com/wiki/Slammin'_Source_Map_Tools), Black Mesa, Nuclear Dawn | ✅ | ✅ | ✅ |
| Hammer++ ≤ 8866 | — | ✅ | ✅ |
| [Strata](https://github.com/StrataSource) Hammer (Portal: Revolution, P2CE, …) | — | ✅ | ✅ |
| GoldSrc: Worldcraft / Hammer 3.x, [J.A.C.K.](https://store.steampowered.com/app/496450/) | — | partly | planned |

Static analysis; live tests so far on Hammer++ 8873 x64. Reports for other builds are very welcome.

## Quick start

1. Download/build `hammer-mcp.exe` and run it — it lives in the tray.
2. **Settings:** pick a mode, address (`localhost` by default), port (`47800`), generate a token for LAN use → **Apply**.
3. **Main:** choose your Hammer process → **Attach** (meta info shows the build, shell channel, open map) → **Start**
   (Start needs an attached process; Apply on the Settings tab lights up only when something changed).
4. Connect your agent — tray menu → *Copy client config*:
   ```bash
   claude mcp add --transport http hammer http://127.0.0.1:47800/mcp
   # LAN:   ... http://10.0.0.5:47800/mcp --header "Authorization: Bearer <token>"
   ```
5. Open a map in Hammer and **save it** (sessions are bound to the saved `mapversion`). Ask away.

## Tools for the agent

| Tool | What it does |
|---|---|
| `hammer_status` | Which Hammer is attached, its build, shell channel, open map, saved `mapversion`, allowed mode |
| `map_summary` | Saved `.vmf`: versions, entity/brush counts, classes, skybox, visgroups |
| `list_entities` · `get_entity` · `find_io` | Saved map: entities with keyvalues and outputs, who triggers whom |
| `game_configs` · `fgd_class` · `fgd_search` | Hammer's game config and the game's FGD as an entity reference (keyvalues, choices, inputs, outputs) |
| `validate_map` | Saved map vs FGD: unknown classes, outputs to missing names, unknown inputs / outputs, extra keyvalues |
| `check_assets` | Materials, models, overlays, decals, skybox of the saved map vs the game's search paths (gameinfo.txt, folders, VPKs) |
| `compile_sequences` · `compile_log` · `leak_trace` | Hammer's own Run Map sequences (CmdSeq.wc / Hammer++ / Strata), parsed compile log, leak path from the .lin |
| `bsp_entities` | Entities as vbsp wrote them into the compiled .bsp (entity lump), outputs included |
| `run_compile` | Runs one of those sequences (vbsp / vvis / vrad, copy .bsp); edit modes only |
| `create_entity` | Point entity at x y z, with keyvalues |
| `set_keyvalue` · `rotate_entity` · `delete_entity` | Edit the entity of a class at an origin |
| `save_map` · `undo` · `redo` | Hammer menu commands |
| `open_run_map_dialog` · `open_check_for_problems` | Open those Hammer dialogs for the user |

**Modes:** *Auto* (live edits where a shell channel exists) · *Basic* (read-only) · *Shell* (live edits + menu) ·
*Advanced* (planned: injected adapters for live reading, selection, camera, geometry).

### Limits of the shell channel
It is write-only and entity-level: entities are addressed by **class + origin** (no ids), brushes and brush entities
can't be created, there is no "move", and unsaved state can't be read back — the server reads the saved `.vmf`.
The planned *Advanced* mode lifts these limits for selected builds.

## Building

```bash
cd src
dotnet build HammerMcp.sln
dotnet test --solution HammerMcp.sln
```
.NET 10 SDK. App and server target `net10.0-windows` (they build on Linux via `EnableWindowsTargeting`, run on Windows 10/11 x64).

```
src/src/HammerMcp.Core      formats (VMF with outputs, RMF/MAP via Sledge, tolerant FGD parser incl. Hammer++/GMod
                            extensions, VMT, Hammer game configs), map validation, build registry, menu ids
src/src/HammerMcp.Shell     shell channel (WM_COPYDATA), menu commands (WM_COMMAND), sessions, process discovery
src/src/HammerMcp.Server    MCP server (Streamable HTTP, Bearer token) and tools
src/src/HammerMcp.App       hammer-mcp.exe — WinForms window + tray
src/tests/HammerMcp.Tests   unit tests
src/registry/hammers.json   known Hammer builds
src/native/                 C++ injector and adapters (planned, CMake)
```

## Roadmap

- [x] Shell channel: entities, keyvalues, rotation, deletion · menu commands · build registry · tray app · LAN + token
- [x] Map reading: entities, outputs, I/O graph; FGD reference from Hammer's own game config; map validation; ambiguity checks before edits
- [x] Compile pipeline: Hammer's Run Map sequences, run_compile, parsed logs, leak tracing; asset checks via gameinfo.txt + VPK
- [ ] Release 0.1: single-file `win-x64` build
- [ ] *Advanced* mode: C++ inject adapters per build (live read, selection, camera, geometry, in-editor UI)

## Releases

Tag `vX.Y.Z[-suffix]` on GitHub → the `release` workflow builds a self-contained single-file `hammer-mcp.exe`
(win-x64, ~65 MB) and attaches a zip to a GitHub release (`-alpha` / `-beta` tags become pre-releases).
Components and licenses: [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

## Credits & legal

Built by [EpicMorg](https://github.com/EpicMorg). Shell-channel protocol as published by Valve in the
[Source SDK 2013](https://github.com/ValveSoftware/source-sdk-2013) (`src/public/editor_sendcommand.{h,cpp}`).
Thanks to [ficool2](https://github.com/ficool2) for [Hammer++](https://github.com/ficool2/HammerPlusPlus-Website) and to the
[Valve Developer Community](https://developer.valvesoftware.com/wiki/Hammer).
Hammer-MCP contains no Valve source code and no leaked code; it talks to editors only through their public, built-in
interfaces. Not affiliated with or endorsed by Valve. Hammer, Source and related marks belong to Valve Corporation.

MIT — see [LICENSE](LICENSE).
