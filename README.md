<p align="center">
  <img src="docs/media/hammer-mcp-red-green-v2.png" alt="Hammer-MCP" width="200">
</p>

<h1 align="center">Hammer-MCP</h1>

<p align="center">
  <b>Let AI agents see and edit maps in Valve Hammer Editor — live.</b><br>
  An <a href="https://modelcontextprotocol.io">MCP</a> server for Hammer (Source 1), <a href="https://github.com/ficool2/HammerPlusPlus-Website">Hammer++</a> and friends.
</p>

<p align="center"><b>English</b> · <a href="README.ru.md">Русский</a></p>

<p align="center">
  <img alt="status" src="https://img.shields.io/badge/status-beta-yellow">
  <img alt="platform" src="https://img.shields.io/badge/platform-Windows%2010%2F11%20x64-0078D6">
  <img alt=".NET" src="https://img.shields.io/badge/.NET-10-512BD4">
  <img alt="MCP" src="https://img.shields.io/badge/MCP-Streamable%20HTTP-6f42c1">
  <img alt="license" src="https://img.shields.io/badge/license-MIT-green">
  <a href="https://glama.ai/mcp/servers/EpicMorg/Hammer-MCP"><img alt="Glama MCP server score" src="https://glama.ai/mcp/servers/EpicMorg/Hammer-MCP/badges/score.svg"></a>
</p>

---

Ask your agent *"put a warm light at each corner of the room and name them by the scheme"* — and watch the entities
appear in the Hammer you already have open. No plugins, no patched editor: Hammer-MCP talks to Hammer through the
**shell channel** that Valve built into the editor 20 years ago (`Worldcraft_ShellMessageWnd`, the mechanism behind
[`editor_sendcommand.cpp`](https://github.com/ValveSoftware/source-sdk-2013/blob/master/src/public/editor_sendcommand.cpp) in the Source SDK) and through Hammer's own menu commands.

<p align="center">
  <img src="docs/media/demo/demo.gif" alt="An agent spawns a prop in Hammer++ and spins it, live" width="720"><br>
  <sub>An agent spawns a <code>prop_dynamic</code> in Hammer++ and spins it - live, over MCP (<a href="docs/media/demo/demo.mp4">video</a>).</sub>
</p>

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

Verified live on **[Hammer++](https://github.com/ficool2/HammerPlusPlus-Website) 8873** x64 ([TF2 Classified](https://store.steampowered.com/app/3545060/)) and x86 (Source SDK 2013 SP mod), from an agent on another machine over the LAN:

| | |
|---|---|
| ✅ Create point entities with keyvalues (`light`, `info_target`, `prop_dynamic` with a model, …) | ✅ Rotate entities (a spinning locker at ~28 edits/s, Hammer stays responsive) |
| ✅ Set keyvalues, delete entities | ✅ Edits land in Hammer's **undo** history — Ctrl+Z works |
| ✅ Save / undo / redo / Run Map / Check for Problems via menu commands | ✅ Map summary from the saved `.vmf` (entities, brushes, classes, skybox…) |
| ✅ Recognises the running Hammer build by hash (registry of 179 known builds) | ✅ Token auth, localhost by default |
| ✅ Map knowledge: entities, outputs, I/O graph, validation against the game's FGD | ✅ Compile with Hammer's own Run Map sequences, parsed log, leak trace, compiled `.bsp` entities |

### Screenshots

| Pick a Hammer process | Attached: build, shell channel | Live log of the demo |
|---|---|---|
| <img src="docs/media/screenshot-1.png" width="260"> | <img src="docs/media/screenshot-2.png" width="260"> | <img src="docs/media/screenshot-3.png" width="260"> |

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
| Source 2 Hammer (CS2, Dota 2, …) — recognised, `.vmap` and FGD read | — | — | ✅ read |
| GoldSrc: Worldcraft 1.1–3.3, Hammer 3.4 / 3.5 and its mods (Neon, Neo, CSM) — recognised; [J.A.C.K.](https://store.steampowered.com/app/496450/) | — | partly | RMF / MAP read |

Static analysis; live tests so far on Hammer++ 8873 (x64 and x86). Reports for other builds are very welcome.

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

**Oh My Pi (OMP):** choose **Copy client config → Oh My Pi** in the tray. Paste or merge the `mcpServers.hammer`
entry into your project's `.omp/mcp.json` or your user config `~/.omp/agent/mcp.json`. Then run `/mcp reload`
and `/mcp test hammer` in OMP. Keep the tray server running; the copied entry includes the bearer token if configured.
For stdio instead, put the example below in the same file.

### Without the tray app: `hammer-mcp-cli`

The release also has `hammer-mcp-cli.exe` - the same server for MCP clients that start servers themselves (stdio):

```json
{ "mcpServers": { "hammer": { "command": "C:\\Tools\\hammer-mcp-cli.exe", "args": ["--stdio", "--auto"] } } }
```

`--auto` attaches to the first running Hammer with a shell channel, `--pid N` to a given process; `--mode Basic` is read-only;
`--http --port 47800` serves HTTP like the app. A `Dockerfile` builds it for MCP catalogs (Hammer itself needs Windows).

## Tools for the agent

| Tool | What it does |
|---|---|
| `hammer_status` | Which Hammer is attached, its build, shell channel, open map, saved `mapversion`, allowed mode |
| `viewport_capture` | PNG images of the active map's 3D view (default), `2d` panes, or `all` in Valve Hammer and Hammer++ |
| `map_summary` | Saved `.vmf`: versions, entity/brush counts, classes, skybox, visgroups |
| `list_entities` · `get_entity` · `find_io` | Saved map: entities with keyvalues and outputs, who triggers whom |
| `game_configs` · `fgd_class` · `fgd_search` | Hammer's game config and the game's FGD as an entity reference (keyvalues, choices, inputs, outputs) |
| `validate_map` | Saved map vs FGD: unknown classes, outputs to missing names, unknown inputs / outputs, extra keyvalues |
| `check_assets` | Materials, models, overlays, decals, skybox, sounds and sound script names of the saved map vs the game's search paths (gameinfo.txt, folders, VPKs) |
| `compile_sequences` · `compile_log` · `leak_trace` | Hammer's own Run Map sequences (CmdSeq.wc / Hammer++ / Strata / J.A.C.K.), parsed compile log, leak path from the .lin |
| `bsp_entities` | Entities as vbsp wrote them into the compiled .bsp (entity lump), outputs included |
| `bsp_visibility` · `bsp_compare` | Offline Source 1 BSP audit: rank visibility hotspots or compare two builds at identical world coordinates |
| `source2_console` | Source 2 only: allowlisted developer console commands over VConsole2 (open maps, force autosave, help) |
| `run_compile` | Runs one of those sequences (vbsp / vvis / vrad, copy .bsp); edit modes only |
| `create_entity` | Point entity at x y z, with keyvalues |
| `set_keyvalue` · `rotate_entity` · `delete_entity` | Edit the entity of a class at an origin |
| `list_live_brushes` | Unsaved Source 1 geometry: brush IDs, owning entities, materials, and plane-point bounds |
| `move_brush` · `scale_brush` · `delete_brush` | Edit one brush by live VMF ID or the current selection |
| `save_map` · `undo` · `redo` | Hammer menu commands |
| `open_run_map_dialog` · `open_check_for_problems` | Open those Hammer dialogs for the user |
| `find_assets` | Find models, materials, textures, sounds, sound script names, sprites, particles by name in the game's files and VPKs (GoldSrc: files and WAD textures / decals); returns what to write in the entity |
| `user_requests` · `wait_for_user` · `say_to_user` | The chat in Hammer (with the adapter): read the user's lines, wait for an answer (up to 10 min), answer them |
| `pending_changes` | Approval mode: what is waiting for the user and what they decided |

**Modes:** *Auto* (best available: the adapter if you connected it, otherwise the shell channel, otherwise read-only) ·
*Basic* (read-only) · *Shell* (live edits + menu through the shell channel only) · *Advanced* (strict adapter-only mode, for debugging).

For offline optimization, run `bsp_visibility` against an uncompressed Source 1 BSP built with VVIS. The ranking can
include exterior or unreachable clusters; use known playable coordinates for point queries. Call `bsp_compare` with
`beforePath`, `afterPath` and the same `points` (`{x,y,z}`) in both builds. Use identical compile settings. Counts cover
distinct world-model faces in the PVS, not props, brush entities, or FPS. Neither tool writes files or launches the game.

For visual inspection in OMP or another image-capable client, call `viewport_capture` (for example with
`{"views":"all","maxSize":1024}`). It shows unsaved changes from the current camera without changing the camera,
map, or render mode. Keep Hammer restored; if stock Hammer shows no 3D frame, bring it to the foreground and aim
at the map. Requires Windows 10 version 1607 or newer. The tool does not capture the desktop and is not available in Advanced mode.

### Live brushes (Valve Hammer and Hammer++)

Use **Auto** or **Shell** mode. For manual targeting, select one solid in Hammer and omit `brushId`. For automatic
targeting, call `list_live_brushes` (optionally filter by `material`), then pass a visible brush's `id` as `brushId`.
`move_brush` takes a relative offset in Hammer units; `scale_brush` takes positive X/Y/Z factors.

These tools use the standalone editor's standard English dialogs and status bar. Keep the status bar visible and
don't switch documents or edit the selection while a call runs. Stock Hammer can refuse an ambiguous selection;
pass an explicit ID in that case. With approval enabled, the original brush is checked again at apply time.
Brush edits use Hammer's native Undo history when enabled. Listing exports a temporary snapshot of the live map;
it does not save over or reload the open VMF. For the copy on disk, use `save_map` explicitly.

### The adapter (0.7+)
**Connect adapter** in the app (Auto or Advanced) injects a small native adapter into the attached Hammer — only when
you press the button, only into a standalone Hammer exe of a known build. It adds an **MCP** menu to Hammer: pause the
agent, **chat with the agent**, live **activity log**, **pending changes** with check boxes (approval mode), undo the
agent's step, status, disconnect. Working in OpenCode? The app's **OpenCode** tab sends your chat lines into the
OpenCode session, so the agent there wakes up and answers. **Disconnect** unloads it; Hammer keeps running. Live reading of the selection,
camera and geometry through the adapter comes next. The `native` folder of the zip must stay next to `hammer-mcp.exe`.

### Limits of the shell channel
It is write-only and entity-level: entities are addressed by **class + origin** (no ids), brushes and brush entities
can't be created, there is no "move", and unsaved state can't be read back — the server reads the saved `.vmf`.
Outputs set with `set_keyvalue` are stored by Hammer as plain keyvalues: they are not shown on the Outputs tab, but
vbsp writes them into the `.bsp` as real outputs and they fire in game.
The adapter is meant to lift these limits for selected builds (next releases). It is only ever injected into a standalone
Hammer executable of a known build (`hammer.exe`, `hammerplusplus.exe`) — **never into a game process**: Source 2
Hammer runs inside `cs2.exe` / `dota2.exe`, and touching a VAC-protected process can get a Steam account banned.

## Safety and VAC

Hammer-MCP is meant to be safe to run next to games with Valve Anti-Cheat.

- **Source 1, Hammer++, Strata, GoldSrc:** Hammer is its own process (`hammer.exe`, `hammerplusplus.exe`, …), not a
  game. The shell channel and menu commands are window messages to that editor process; the game you start from
  Run Map is never touched.
- **Source 2 (CS2, Dota 2, HL: Alyx):** Hammer runs *inside* the game process (`cs2.exe`, …). Hammer-MCP only does what
  Task Manager or OBS do with it — reads the exe path, window titles and command line (`OpenProcess` with
  query-only rights) — and reads map and game files from disk (`.vmap`, FGD). It never writes to the game's memory,
  hooks or injects anything.
- **Injection** (the adapter) happens only when you press **Connect adapter**, and is limited by code (the app's policy
  and again in the native injector) to standalone Hammer executables of a known build — never game processes, never
  Source 2, never unknown builds. Without the button nothing is injected.
- **`-insecure` is not required.** Workshop Tools (`-tools`) already behave like an insecure client: CS2 refuses VAC
  servers in that mode (checked live, 2026-11). You may add `-insecure` to the launch options for extra peace of
  mind, but remove it afterwards, or you will not be able to join matchmaking / VAC servers.

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
src/src/HammerMcp.Cli       hammer-mcp-cli.exe — the server without the tray app (stdio / HTTP)
src/tests/HammerMcp.Tests   unit tests
src/registry/hammers.json   known Hammer builds
src/native/                 C++ injector and adapter (CMake; MSVC or clang-cl), see src/native/PROTOCOL.md
```

## Roadmap

- [x] Shell channel: entities, keyvalues, rotation, deletion · menu commands · build registry · tray app · LAN + token
- [x] Map reading: entities, outputs, I/O graph; FGD reference from Hammer's own game config; map validation; ambiguity checks before edits
- [x] Compile pipeline: Hammer's Run Map sequences, run_compile, parsed logs, leak tracing; asset checks via gameinfo.txt + VPK
- [x] Releases 0.1 – 0.8: single-file `win-x64` and `win-x86` builds — see [CHANGELOG.md](CHANGELOG.md)
- [x] Adapter, layer 1 (0.7): MCP menu in Hammer, chat, live activity log, approval list
- [ ] Adapter, layer 2: live read of the selection, camera, geometry (per-build profiles); docked panels in Hammer

## Releases

Tag `vX.Y.Z[-suffix]` on GitHub → the `release` workflow builds a self-contained single-file `hammer-mcp.exe`
plus `hammer-mcp-cli.exe` and the native adapter (win-x64 and win-x86) and attaches the zips to a GitHub release (`-alpha` / `-beta` tags become pre-releases).
Release notes come from [CHANGELOG.md](CHANGELOG.md). Contributing, new Hammer builds, bug reports: [CONTRIBUTING.md](CONTRIBUTING.md); security: [SECURITY.md](SECURITY.md). Components and licenses: [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

## Links

| Where | What |
|---|---|
| [GitHub releases](https://github.com/EpicMorg/Hammer-MCP/releases) | Downloads (win-x64 / win-x86 zips: `hammer-mcp.exe`, `hammer-mcp-cli.exe`, `native\`) and release notes |
| [Valve Developer Community](https://developer.valvesoftware.com/wiki/Hammer_MCP_Server) ([RU](https://developer.valvesoftware.com/wiki/Ru/Hammer_MCP_Server)) | Article about the tool |
| [GameBanana](https://gamebanana.com/tools/24946) | Tool page for mappers |
| [HL-Lab](https://hl-lab.ru/rus/files_source/Hammer_MCP_Server) | Tool page on the Russian Half-Life / Source mapping site |
| [Glama](https://glama.ai/mcp/servers/EpicMorg/Hammer-MCP) | MCP server listing and score |
| [Issues](https://github.com/EpicMorg/Hammer-MCP/issues) | Bugs, unknown Hammer builds ([how to report](CONTRIBUTING.md)) |

## Credits & legal

Built by [EpicMorg](https://github.com/EpicMorg). Shell-channel protocol as published by Valve in the
[Source SDK 2013](https://github.com/ValveSoftware/source-sdk-2013) (`src/public/editor_sendcommand.{h,cpp}`).
Thanks to [ficool2](https://github.com/ficool2) for [Hammer++](https://github.com/ficool2/HammerPlusPlus-Website) and to the
[Valve Developer Community](https://developer.valvesoftware.com/wiki/Hammer).
Hammer-MCP contains no Valve source code and no leaked code; it talks to editors only through their public, built-in
interfaces. Not affiliated with or endorsed by Valve. Hammer, Source and related marks belong to Valve Corporation.

MIT — see [LICENSE](LICENSE).
