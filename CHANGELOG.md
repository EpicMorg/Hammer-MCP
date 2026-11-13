# Changelog

**English** · [Русский](CHANGELOG.ru.md)

All notable changes to Hammer-MCP. Versions follow [SemVer](https://semver.org/); `-alpha` releases are pre-releases.

## [Unreleased]

### Added
- **Source 2 maps**: `.vmap` (DMX, read with KeyValues2 / Datamodel.NET) - entities in world space with keyvalues and outputs, hidden nodes, summary; `map_summary`, `list_entities`, `get_entity`, `find_io`, `validate_map` work on Source 2 maps. Prefabs and instances are not expanded yet.
- **Source 2 FGD**: class metadata blocks (`{ entity_tool_name = ... }`, shown by `fgd_class`), per-member `[ ]` / `{ }` metadata, `@OverrideClass`, escaped quotes, includes from `game/core/`; model / VData definitions are skipped.
- Source 2 Workshop Tools processes (`cs2`, `dota2`, `hlvr`, ... with `tools/hammer.dll`) are listed for Attach; an absolute map path anywhere in the window title is recognised (`.vmap` included). Verified live on CS2.
- Source 2 game data without a GameConfig.txt: one config per mod with `game/<mod>/<mod>.fgd` (CS2 `csgo`: 469 classes, Dota 2 `dota`: 427), picked by the open map's `content/<mod>` folder (`csgo_addons` → `csgo`).
- Inject policy for the future Advanced mode (`InjectPolicy`): only standalone Hammer executables of a known build with an adapter; never game processes (VAC / anti-cheat), never Source 2, never unknown builds.
- Verified live on CS2 Workshop Tools (2026-11-13): attach to `cs2.exe`, map name from the title resolved in `content/csgo_addons/<addon>/maps`, `map_summary` / `list_entities` / `get_entity` / `find_io` / `validate_map` / `fgd_class` on a 69 MB `.vmap` (format 40); new CS2 `hammer.dll` in the registry; process command line in `hammer_status`.
- A map outside every MapDir uses the game config of the Hammer's own install (the only config whose game folder is inside it and has a gameinfo.txt, e.g. `tf` for the TF2 Hammer) instead of asking for `configName`.

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

[0.2.0]: https://github.com/EpicMorg/Hammer-MCP/compare/v0.1.0-alpha...v0.2.0
[0.1.0-alpha]: https://github.com/EpicMorg/Hammer-MCP/releases/tag/v0.1.0-alpha
