# Editor support

[Русская версия](editors.ru.md) · [Shell channel](https://github.com/EpicMorg/HammerShell/blob/master/docs/shell-channel.md)

Hammer-MCP recognises the editor by the hash of its exe/DLL and looks it up in the
[build registry](../src/registry/hammers.json) (190 builds). What it can do then depends on the editor:

- **Shell edits**: create / change / delete entities through Hammer's [shell channel](https://github.com/EpicMorg/HammerShell/blob/master/docs/shell-channel.md).
- **Menu commands**: Save, Undo, Redo, Run Map, Check for Problems as `WM_COMMAND` messages to the main window.
- **Adapter**: the optional DLL loaded into Hammer on *Connect adapter* (never into a game, Source 2 or an unknown
  build). It adds the *MCP* menu (pause, pending changes, chat) and reads the live selection and 3D camera.
- **Selection / Camera**: what is selected right now, and where the 3D view looks (needs the adapter and a memory
  layout for the exact build). Ships with Hammer-MCP 0.9.
- **Map files**: reading the saved map, Hammer's autosaves, game configs, FGDs and compile logs. This works for every
  editor below.

● checked live · ○ found statically (offsets or ids derived from the binaries, not yet run) · — not available

| Editor | Builds | Shell edits | Menu commands | Adapter | Selection | Camera |
| --- | ---: | :---: | :---: | :---: | :---: | :---: |
| Hammer++ 8867–8873, x86 and x64 | 41 | ● | ● | ● | ● | ● |
| Hammer++ 8846–8866, x86 and x64 | 78 | — | ● | ● | ● ¹ | ● |
| Source Level Editor 2.104–3.129 (all GitHub releases) | 8 | ● | ● | ● | ● | ● ² |
| Stock Hammer, 2013 branch and newer: SDK 2013 SP / MP, HL2, TF2, CS:S, HL1 Source, GMod, ZPS, Portal Prelude RTX, dedicated server; x86 and x64 | 16 | ● | ● | ● | ● | ● |
| Stock Hammer, older branches: Episode One, Orange Box, Source 2007 / 2009, L4D, L4D2, Alien Swarm, Portal 2, CS:GO, Insurgency, Nuclear Dawn, PVKII and others | 17 | ○ ³ | ○ ³ | ○ ³ | ○ ³ | ○ ³ |
| Psycho-A's SDK repack (old SDK Hammers) | 8 | ○ | ○ | ○ | ○ | ○ |
| Slammin' Hammer 5.2, Black Mesa Hammer | 2 | ○ | ○ | ○ | ○ | ○ |
| Strata Hammer (Portal: Revolution), x64 | 1 | — | ○ | — | — | — |
| Source 2 Hammer (CS2, Dota 2) | 3 | — | — | — | — | — ⁴ |
| GoldSrc: Valve Hammer Editor 3.4 / 3.5 and its mods (CSM, Neo, Neon) | 7 | — | ● | ● | ● | ● |
| GoldSrc: Worldcraft 1.1–3.3 | 7 | — | ○ | ○ | ○ ⁵ | ○ ⁵ |
| J.A.C.K. / Jackhammer, x64 | 2 | — | — | — | — | — |

¹ Live-checked on 8862 x86. On 8846 the camera works, but the selection layout failed the live check and is being
fixed (8846–8849).
² Live-checked on 3.129; the other releases share its layouts by static analysis.
³ Nuclear Dawn is checked live (all five). L4D2 and Portal 2 Hammer start only through Steam as the Authoring Tools,
so their live check is still to do.
⁴ Source 2: maps are read from `.vmap` files, and the unsaved state of the open map through the game's developer
console (VConsole2). Live-checked on CS2.
⁵ Worldcraft 3.3 only.

Live checks so far (0.9 development builds; each one covers shell edits where the build has the channel, the adapter,
the selection with entities and a brush, the camera and a mouse-wheel step):

- Hammer++: 8873 TF2 x64, 8862 x86 (no shell channel), 8846 x86 (camera only).
- Stock Hammer: TF2 x64, HL2 (2024) x86, Garry's Mod x86 and x64, Counter-Strike: Source x64, Nuclear Dawn.
- Source Level Editor 3.129; Valve Hammer Editor 3.4 and 3.5; CS2 (map reading incl. unsaved state).

Static rows become live ones as they are checked; the registry records the details per build.

## Notes per editor

- **Hammer++** adds the shell channel only from build 8867 (2024-07-02). The window has the same name as in stock
  Hammer.
- **Stock Hammer** and all its forks (Hammer++, Slammin', Black Mesa, Strata, …) use the same command ids for the key
  menu items, so menu commands work the same way across 152 Source 1 builds.
- **Strata Hammer** has a Qt-based interface and no shell channel; the adapter is never loaded into it. Hammer-MCP reads
  its maps, configs and Run Map sequences.
- **GoldSrc editors** have no shell channel at all; it appeared with Source Hammer 4.x. Hammer-MCP reads `.rmf` /
  `.map` maps, WAD lists and ZHLT / VHLT compile logs.
- **J.A.C.K.** and Jackhammer are separate editors (not Valve code); Hammer-MCP reads their game configs, maps and
  compile settings.
