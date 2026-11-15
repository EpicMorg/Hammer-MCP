# Changelog

**English** · [Русский](CHANGELOG.ru.md)

All notable changes to Hammer-MCP. Versions follow [SemVer](https://semver.org/); `-alpha` releases are pre-releases.

## [0.8.0] - 2026-11-15

### Added
- **GoldSrc game configs**: the binary `GameCfg.wc` next to Worldcraft / Hammer 3.x (versions 1.3 and 1.4) is read, so `game_configs`, `fgd_*`, `compile_sequences` and `find_assets` work with GoldSrc editors too. A config gives the mod folder (`gameDir`), the game it falls back to (`valve`), FGDs (relative ones against the editor folder), map / bsp folders and the compile tools (`$csg_exe` included).
- **`find_assets` for GoldSrc**: models (`models/scientist.mdl`; texture and sequence-group files like `scientistt.mdl`, `scientist01.mdl` are left out), sprites (`sprites/glow01.spr`), sounds (`ambience/wind1.wav`) from the mod and `valve` with their `_addon` / `_downloads` folders, plus textures by name from the WADs in those folders and decals from `decals.wad` (`{BLOOD1`; a wildcard matches names with or without the `{` `!` `+0` `-0` `~` prefix). Utility WADs (fonts, gfx, cached, spray / temp decals) are skipped. PAKs (`pak0.pak`, `pak1.pak`, ...) in those folders are searched too.
- **The texture WAD list of Worldcraft / Hammer 3.x** (Options > Textures) is read from the editor's registry key and searched first; the key of every known GoldSrc editor is in the build registry (`settingsKey`: Worldcraft 1.1-2.0, Worldcraft 2.1-3.3, Hammer 3.4/3.5 and Neo, CSM, Neon each keep their own).
- An old `GameCfg.wc` (version 1.3, Worldcraft 1.6-2.x) has no mod folder: `<game exe folder>\valve` (or `id1`) is used and reported as a guess.
- **J.A.C.K. / Jackhammer game configs** (`VDKGameCfg.ini` next to the exe): mod and base folders, FGDs, compilers, and the texture WAD list (`Parcel*`), which `find_assets` searches first.
- **J.A.C.K. / Jackhammer Run Map profiles** (`VDKRunCfg.ini` next to the exe) in `compile_sequences` and `run_compile`: every profile (Default, Half-Life (Full), (FastVis), ...) with its steps - change directory, copy / delete / rename file, compilers, the game - and `$csg_exe` / `$bspext`. A step's "check file" (`FileCheck`) is enforced: `run_compile` stops when the step did not write it during this run (hlbsp writes no `.prt` when the map leaks; a file left from an earlier compile does not count). Delete / rename steps and copy targets may only touch files in the map folder or the game's maps folder. The same goes for "ensure file post-exists" in Hammer's sequences.
- **GoldSrc compile logs** in `compile_log` / `run_compile`: `-----  BEGIN  hlcsg -----` stages with timings and ZHLT / VHLT leaks (`=== LEAK in hull 0 ===`, the leaking entity); the compile log is also looked for in the game's maps folder, where J.A.C.K. compiles. GoldSrc compilers read the `.map` the editor exports: `run_compile` refuses to start when that file is missing or older than the open `.rmf` / `.jmf` and says how to export it.
- **`check_assets` for GoldSrc maps** (`.rmf`, `.map`, `.jmf`): face textures against the WADs (the game's and those in worldspawn `wad`), `infodecal` decals against `decals.wad`, models / sprites / sounds in any keyvalue and the sky (`gfx/env/<skyname>*.tga`) against the folders and PAKs; WADs listed in the map but found nowhere are reported. Compiler tool textures (CLIP, ORIGIN, NULL, SKIP, HINT, ...) are not checked.
- **Sound scripts (Source 1)**: `find_assets` also finds named sounds (`Ambient.Wind`, `Weapon_Pistol.Single`) from the scripts that `scripts/game_sounds_manifest.txt` lists (`precache_file`, `preload_file`, ...; `#include` / `#base` followed), loose or inside VPKs; kind `soundscript`, each hit with its waves. Without a manifest every `scripts/game_sounds*.txt` is read. The first definition of a name wins, as in the engine. Source 2 sound events (`.vsndevts`) are not read yet.
- **`check_assets` checks sounds (Source 1)**: sound files in any keyvalue (`ambient/wind1.wav`, sound characters like `)` or `*` stripped) against `sound/`, and the sound script name in `ambient_generic`'s `message` against the sound scripts and the map's own `maps/<map>_level_sounds.txt`.
- Files inside VPKs and PAKs can now be read (not only listed).
- **OpenCode bridge** (new OpenCode tab in the app): each line you write in Hammer's chat (MCP > Chat) also goes into your OpenCode session as a message, so an agent working in OpenCode wakes up and answers without you switching windows. OpenCode 2.x (`/api/session/{id}/prompt`, queued after the agent's current turn) and 1.x (`prompt_async`); Basic auth; the session is the one you used last unless you set its id; **Test** shows where the lines go. A line the agent is already waiting for in `wait_for_user` goes there instead, and a delivered line is not handed out again by `user_requests`. A password that would go over plain http to another machine is warned about (Test) and needs confirmation (Save).

### Fixed
- Left 4 Dead and later games (Portal 2, CS:GO, ...) mount `pak01_dir.vpk` of each search path folder without listing it in `gameinfo.txt`; it is now mounted too (only when the `gameinfo.txt` lists no VPKs, as these games do - Source 2013 games list theirs), so `find_assets` and `check_assets` see that content.
- Content is looked up in the order of the `gameinfo.txt` search paths: a VPK listed before a folder (TF2: `tf/tf2_misc.vpk` before `tf`) wins over the folder's loose files, and an auto-mounted `pak01_dir.vpk` over its folder. Before, every loose folder came first, so `find_assets` could name the wrong copy.
- GoldSrc Hammer 3.x shows only the file name in its title when the map window is not maximized (`Valve Hammer Editor - tut1.rmf`): the full path is now taken from the process command line or found in the configs' map folders, so map tools work there.
- `find_assets` (GoldSrc): a model whose name merely ends in `t` or two digits (`foot.mdl` next to `foo.mdl`) is no longer hidden as a texture / sequence-group file - loose files are checked by their header.
- The build registry called a genuine Valve Hammer 3.5 build "HL-Lab" (`goldsrc-hammer-3.5-hllab` is now `goldsrc-hammer-3.5-b`).
- Entities inside groups of `.rmf` / `.jmf` maps were not listed.
- App: a second click on Start / Stop while the server was still starting (a double click) stopped it again a moment later.

## [0.7.0] - 2026-11-15

### Added
- **The adapter**. Nothing is injected unless you press **Connect adapter**. The app's **Connect adapter** button (MCP Mode Auto or Advanced; known standalone Hammer builds) injects a small native adapter into the attached Hammer - one DLL per bitness (`hmcp-adapter-x86/x64.dll`) that finds its build itself; the server sends it the build profile from its registry. It adds an **MCP** menu (with icons) to Hammer's main menu bar: pause / resume the agent (every edit is refused while paused - shell channel and menu commands too), **Chat with the agent...** (a chat window: your lines reach the agent in `hammer_status.userRequests` / `user_requests`, once; the agent answers with the new `say_to_user` tool), **Activity log...** (a live window), **Pending changes** (a list with check boxes), status, disconnect, about. Auto never injects by itself: it uses the adapter when you connected it, and after Attach connects to an adapter that is already loaded (e.g. the app was restarted). **Advanced** is now strict (for debugging): everything that changes Hammer goes through the adapter only, which cannot edit yet - so edits, menu commands and compiles are refused there. Basic and Shell do not use the adapter. Never injected into a game process (VAC / anti-cheat), Source 2, or an unknown build; the native injector re-checks the policy before it opens the process. `hammer_status` reports `adapter` and `agentPaused`; `user_requests` also returns `recentChat` (the last 10 chat lines from both sides). The chat is a side channel (it says so on top): the agent reads it between its steps, and the new `wait_for_user` tool lets it wait there for your answer (up to 10 minutes) - nothing in MCP can wake an idle agent, so otherwise write to it in its own window. The release zip has a `native` folder next to `hammer-mcp.exe` (both injectors and adapters) - keep it there.

- **x86 build**: every release now also has `hammer-mcp-v<version>-win-x86.zip` (same layout as win-x64) for 32-bit Windows; x86 zips were added to the earlier releases too. The 32-bit app works with 64-bit Hammer as well.

- **`find_assets`**: find content by name in the game of the attached Hammer - loose files and VPKs from the search paths (Source 1: `gameinfo.txt`; Source 2: `game/<mod>`, `content/<mod>`, `core`, compiled `*_c` reported as Hammer writes them): models, materials, textures, sounds, sprites, particles. A part of the path or a wildcard (`props_2fort/*locker*`, `ambient/wind*`); each hit has `keyValue` (what to write in the entity: material `tools/toolsnodraw`, sound `ambient/wind1.wav`) and the source it comes from (the first one wins). GoldSrc (WADs, its game config) comes later.

- App: **Settings > Logs** - save the log to daily files (`hammer-mcp-yyyy-MM-dd.log`, on by default, folder `%APPDATA%\EpicMorg\Hammer-MCP\logs` or your own), Browse... and Open folder. The window is a bit taller.

- **Approval mode** (app: **Ask me before agent edits**): the agent's edits - create / set_keyvalue / rotate / delete, save_map, undo, redo, run_compile - wait until you apply or reject them: in the **Pending changes** window that pops up (Apply selected / Apply all / Reject all) or in Hammer's **MCP > Pending changes (N)** (with the adapter). The tool call waits up to 2 minutes; a rejected change tells the agent not to repeat it; a late decision is reported by the new `pending_changes` tool. Applied changes run one at a time in the order the agent asked for them; an entity's address is checked again when the change is applied (the map may have changed meanwhile). Pausing the agent rejects everything waiting or queued. At most 20 changes wait at once. `hammer_status` shows `approval` and `pendingChanges`.

- App: tray **Copy client config** has ready-to-paste settings for Claude Code, Codex, OpenCode, Gemini CLI, Cursor, VS Code / Copilot and Claude Desktop (via mcp-remote), with the token if one is set.

- App: **Ask me before agent edits** is remembered between runs (saved as soon as it is toggled).

### Changed
- **Advanced** mode is strict (adapter only, for debugging): edits, menu commands and compiles are refused until the adapter can do them - use **Auto** for work. The **Connect adapter** button works in Auto and Advanced.

### Fixed
- The editor DLL is matched to the exe: in a folder with both Slammin's `hammer.exe` and Hammer++ (Bestiarium), `hammer.exe` was identified as Hammer++.

## [0.6.0] - 2026-11-14

The MCP server without the tray app (`hammer-mcp-cli`, stdio / HTTP) and a container image for MCP catalogs.

### Added
- **`hammer-mcp-cli`**: the MCP server without the tray app - stdio (default) or HTTP, Hammer picked with `--auto` / `--pid`, `--mode`; shipped in the release zip. A `Dockerfile` runs it in a container (it starts and lists its tools without a Hammer - what MCP catalogs such as Glama check).

### Changed
- Public repository: status **beta**, `SECURITY.md` (private vulnerability reports), `CONTRIBUTING.md` (how to report an unknown Hammer build), issue templates, demo GIF / video and screenshots in the README, articles on the Valve Developer Community ([EN](https://developer.valvesoftware.com/wiki/Hammer_MCP_Server), [RU](https://developer.valvesoftware.com/wiki/Ru/Hammer_MCP_Server)).

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

[0.8.0]: https://github.com/EpicMorg/Hammer-MCP/compare/v0.7.0...v0.8.0
[0.7.0]: https://github.com/EpicMorg/Hammer-MCP/compare/v0.6.0...v0.7.0
[0.6.0]: https://github.com/EpicMorg/Hammer-MCP/compare/v0.5.0...v0.6.0
[0.5.0]: https://github.com/EpicMorg/Hammer-MCP/compare/v0.4.0...v0.5.0
[0.4.0]: https://github.com/EpicMorg/Hammer-MCP/compare/v0.3.0...v0.4.0
[0.3.0]: https://github.com/EpicMorg/Hammer-MCP/compare/v0.2.0...v0.3.0
[0.2.0]: https://github.com/EpicMorg/Hammer-MCP/compare/v0.1.0-alpha...v0.2.0
[0.1.0-alpha]: https://github.com/EpicMorg/Hammer-MCP/releases/tag/v0.1.0-alpha
