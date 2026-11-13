# Changelog

**English** · [Русский](CHANGELOG.ru.md)

All notable changes to Hammer-MCP. Versions follow [SemVer](https://semver.org/); `-alpha` releases are pre-releases.

## [Unreleased]

### Added
- **`hammer-mcp-cli`**: the MCP server without the tray app - stdio (default) or HTTP, Hammer picked with `--auto` / `--pid`, `--mode`; shipped in the release zip. A `Dockerfile` runs it in a container (it starts and lists its tools without a Hammer - what MCP catalogs such as Glama check).

## [0.5.0] - 2026-11-14

Brush entities, outputs written as keyvalues, Source 2 prefab variables; Strata and stock TF2 Hammer verified live.

### Added
- **Brush entities** (`trigger_*`, `func_*`) are addressed by the centre of their brushes: Hammer++ and stock Hammer write it as their `origin`; for .vmf files without it the centre is computed from the brush planes (`center` in `list_entities` / `get_entity`). Verified live on Hammer++ and stock TF2 Hammer: `set_keyvalue`, outputs, and `delete_entity`, which removes the entity together with its brushes.
- **Outputs written as keyvalues**: `validate_map` lists them (`outputsAsKeyValues`) and checks their targets and inputs; `get_entity` / `find_io` show them as outputs.
- **vmap prefab variables**: keyvalues bound to prefab variables get the placed prefab's values - defaults, choice groups (e.g. `TowerLocation = middle_tier_3` sets the tower's name and unit) and overrides; a named prefab is an I/O target of the outer map. Dota towers now get their real names. `fixupEntityNames` is not applied (the renaming rule is not confirmed by the data).
- App: right-click menu on the Meta info grid - Copy (cell), Copy row, Copy all.
- `hammer_status` lists all window titles of the process when no open map is found.

### Changed
- Strata (Portal: Revolution x64) verified live: build, game config, FGD, `validate_map`, `check_assets`, Run Map sequences. `$postcompiler_exe` and `$bsp` are expanded; `|all_source_engine_paths|` points to the game root when the game exe lives in `bin\win64`.
- Strata ships `"Autosave Dir" = "\\autosave\\"`, which Hammer rejects (not an absolute path, autosaves disabled); `fromAutosave` now says so instead of looking in a guessed folder.
- A bare map file name in the Hammer title (seen on Strata) is looked up in the MapDir of the Hammer's game configs.
- Source 2 maps are cached by path + write time + size: a big `.vmap` with prefabs is not re-read on every call.

## [0.4.0] - 2026-11-13

Source 2: unsaved Hammer state through the developer console, prefabs expanded; Strata game config and autosaves.

### Added
- **Strata (Portal: Revolution)**: game config from `hammer/cfg/gameconfig.txt` (paths with `..\..` as shipped are normalised), autosave folder from `hammer/cfg/HammerConfig.vdf` (KV3) for `fromAutosave`; `revolution.fgd` reads cleanly (563 classes).
- **Source 2 developer console** (`source2_console`): allowlisted commands over VConsole2 (TCP 127.0.0.1:29000, the channel `vconsole2.exe` uses) - `hammer_map_files`, `hammer_force_autosave`, `hammer_prefab_tree`, `find`, `help`, `status`, …; nothing is injected. Verified live on CS2 (no `-vconsole` needed).
- **vmap prefabs and instances are expanded**: `CMapPrefab` (another `.vmap`, resolved in the map's mod / addon folder, then the stock mod) and `CMapInstance` with their transforms, nested up to 6 levels; their entities get ids from 1000000 and `prefab` / `prefab_node` keyvalues. Dota's `dota.vmap`: 69 entities of its own, 1734 with 67 prefabs.
- **Unsaved Source 2 state**: read tools with `fromAutosave=true` run `hammer_force_autosave` in the developer console and read the newest autosave from `content\_toolsautosave\hammer\<map folder>` (prefabs still resolved in the real content folder). The open map comes from `hammer_map_files` when the window title has none. Verified: the console works without `-vconsole` in Workshop Tools.
- Developer console output is clean: PRNT messages have a 28-byte header (channel, flags, colour, time) before the text, verified on CS2.

## [0.3.0] - 2026-11-13

Source 2 maps and game data (read-only), verified live on CS2 Workshop Tools; stock Hammer verified live on TF2; safety rules around game processes.

### Added
- **Source 2 maps**: `.vmap` (DMX, read with KeyValues2 / Datamodel.NET) - entities in world space with keyvalues and outputs, hidden nodes, summary. `map_summary`, `list_entities`, `get_entity`, `find_io`, `validate_map` work on Source 2 maps; the world is an I/O target (prefab relays). Prefabs and instances are not expanded yet.
- **Source 2 FGD**: class metadata blocks (`{ entity_tool_name = ... }`, shown by `fgd_class`), per-member `[ ]` / `{ }` metadata, `@OverrideClass`, escaped quotes, includes from `game/core/`; model / VData / helpinfo definitions are skipped. CS2 `csgo.fgd`: 469 classes, Dota 2 `dota.fgd`: 427, no warnings.
- **Source 2 game data** without a GameConfig.txt: one config per mod with `game/<mod>/<mod>.fgd`, picked by the open map's `content/<mod>` folder (`csgo_addons` → `csgo`).
- **Source 2 Workshop Tools processes** (`cs2`, `dota2`, `hlvr`, … with `tools/hammer.dll`) are listed for Attach. The map name Hammer shows in its title is looked up in `content/<mod>/maps` and `content/<mod>_addons/<addon>/maps`.
- `hammer_status`: window title, process command line (`insecure` flag) and, for unknown builds, the editor DLL's sha256 for the registry.
- **Inject policy** for the future Advanced mode (`InjectPolicy`): only standalone Hammer executables of a known build with an adapter; never game processes (VAC / anti-cheat), never Source 2, never unknown builds.
- README: **Safety and VAC** section (EN / RU).
- Registry: the CS2 `hammer.dll` of 2026-11-13.

### Changed
- A map outside every MapDir uses the game config of the Hammer's own install (the only config whose game folder is inside it and has a gameinfo.txt, e.g. `tf` for the TF2 Hammer) instead of asking for `configName`.
- Source 2 UI and agent texts: own attach status ("read-only from map and game files"), mode help, menu tools refuse politely, agent instructions.
- Tool descriptions after the live test of stock Hammer (TF2): `set_keyvalue` is not in Hammer's undo history; `rotate_entity` only turns entities with a model - set the `angles` keyvalue for the others.
- FGD types are printed without spaces (`resource:vjs`); Source 2 editor keys (`local.origin`, `useLocalOffset`, …) are not reported as unknown keyvalues.

### Verified live
- **CS2 Workshop Tools**: attach to `cs2.exe`, a 69 MB `.vmap` (format 40) from an addon read and validated. Workshop Tools behave like an insecure client (VAC servers refused).
- **Stock Hammer TF2 x86**: create / keyvalue / delete / save over the shell channel, outputs as keyvalues, `validate_map`, `check_assets` (gameinfo + VPK), `compile_sequences` from the binary `CmdSeq.wc`.

## [0.2.0] - 2026-11-13

Reading maps, game data and the compile pipeline; first live edits of logic; a much larger build registry.

### Added
- **Map knowledge**: `get_entity`, `find_io` (logic graph: who fires what at whom), `validate_map` (unknown classes, keyvalues and outputs not in the FGD, outputs to missing targets, inputs the target does not have, values with double quotes inside).
- **Game data**: `game_configs` (Hammer / Hammer++ game configurations), `fgd_class`, `fgd_search`. Tolerant FGD parser: Hammer++ `@version` / `@ExtendClass`, Garry's Mod `*studio` types, fgdlib-like class redefinition and parse-time inheritance.
- **Formats**: VMF / VMX / VMM / `vmf_autosave` with outputs (`,` or ESC separators) and hidden entities; RMF / MAP / JMF via Sledge.Formats; VMT materials and Hammer game configs via ValveKeyValue.
- **Assets**: `check_assets` - materials, models, sprites, decals, skybox of the saved map checked against `gameinfo.txt` search paths (`|gameinfo_path|`, `|all_source_engine_paths|`, `|appid_N|` via Steam libraries), folders and VPK directories.
- **Compile**: `compile_sequences` (Run Map sequences from `CmdSeq.wc`, Hammer++ and Strata files, variables expanded), `compile_log` (stages, leaks, missing materials, model errors, limits), `leak_trace` (`.lin` leak path), `run_compile` (edit modes only; skips launching the game and the log viewer), `bsp_entities` (entity lump of the compiled `.bsp`, outputs included).
- **Autosaves**: read tools take `fromAutosave` to use the newest autosave when it is newer than the saved map (Hammer++ `AutosaveDir`, stock Hammer `Autosave Dir` from the registry).
- **Build registry**: 179 known editor builds - every Hammer++ release 8846-8873 (all branches), stock Source 1 Hammer of Valve games and SDKs (x86 and x64), mods (Portal Stories: Mel, Aperture Tag, Portal Reloaded, Black Mesa, ...), Slammin' Hammer, Strata (Portal: Revolution), Source 2 (`tools/hammer.dll` of CS2 and Dota 2) and GoldSrc (Worldcraft 1.1-3.3, Hammer 3.4 / 3.5, Neon / Neo / CSM Hammer 3.5). Source 2 and GoldSrc builds are recognised (GoldSrc by the editor exe itself); live editing still needs the shell channel.

### Changed
- The main window title shows the app version; About and the title take it from the exe's version resource (About showed 0.1.0-alpha in local builds).
- `run_compile` / `compile_sequences` quote expanded arguments that contain spaces, like Hammer does (`-game "...\Bestiarium Project\bst"`), and `run_compile` ignores a compile log left over from an earlier run.
- `validate_map` resolves output targets by targetname, then by classname (as the engine does), and accepts classes that exist only at runtime (`player`, ...).
- Outputs are formatted with the invariant culture (`delay 0.5`, not `0,5`).
- A missing `custom/*` search path is no longer reported as a problem.

### Notes
- Outputs set with `set_keyvalue` (`OnTrigger` = `target,Input,param,delay,times`) are stored by Hammer as plain keyvalues: they are not shown on the Outputs tab, but vbsp writes them into the `.bsp` as real outputs and they fire in game (verified with `bsp_entities`).
- Hammer cuts a value with double quotes inside (e.g. `echo "text"`) on load, and a later save keeps the damage; `validate_map` now warns about such lines.

## [0.1.0-alpha] - 2026-11-12

First public pre-release.

### Added
- MCP server over Streamable HTTP (`/mcp`, default port 47800, Bearer token; required when not bound to localhost).
- Tray app (WinForms): pick and attach a running Hammer, start / stop the server, modes (Auto / Basic / Shell / Advanced), bind address, port, token, logs, About.
- Live editing through Hammer's shell channel (`Worldcraft_ShellMessageWnd`): `create_entity`, `set_keyvalue`, `rotate_entity`, `delete_entity` (refused when the address is ambiguous), `undo`, `redo`, `save_map`, `open_run_map_dialog`, `open_check_for_problems`.
- Reading the saved map: `hammer_status`, `map_summary`, `list_entities`.
- Embedded build registry (`registry/hammers.json`, overridable by a file next to the exe).

[0.5.0]: https://github.com/EpicMorg/Hammer-MCP/compare/v0.4.0...v0.5.0
[0.4.0]: https://github.com/EpicMorg/Hammer-MCP/compare/v0.3.0...v0.4.0
[0.3.0]: https://github.com/EpicMorg/Hammer-MCP/compare/v0.2.0...v0.3.0
[0.2.0]: https://github.com/EpicMorg/Hammer-MCP/compare/v0.1.0-alpha...v0.2.0
[0.1.0-alpha]: https://github.com/EpicMorg/Hammer-MCP/releases/tag/v0.1.0-alpha
