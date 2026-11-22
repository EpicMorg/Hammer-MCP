# Hammer shell channel

[Русская версия](shell-channel.ru.md)

The shell channel lets a Windows program send edit commands to a running Source 1 Hammer editor. Valve's public Source SDK includes the sender used by the game's map-editing code. Hammer-MCP uses the same Windows message to update the open map from a separate process. [SDK sender][sdk-sender] · [SDK game-side code][sdk-game]

## HammerShell.Client API for C#

Reference the library from a .NET 10 console application targeting Windows. This command assumes MyTool.csproj is at the repository root.

```sh
dotnet add MyTool.csproj reference src/src/HammerShell.Client/HammerShell.Client.csproj
```

Open and save a test map in one instance of Hammer. Replace `shell_test` and `12` with the map name without its extension and the saved `versioninfo.mapversion`. This program opens a session, checks the version and prints the editor's response.

```csharp
using System;
using HammerShell.Client;

nint hwnd = ShellChannel.FindWindow();
if (hwnd == 0) return;

var channel = new ShellChannel(() => hwnd);
using var session = ShellSession.Begin(channel, "shell_test", 12);
if (session is null) return;

var check = ShellCommands.MapCheckVersion(session.MapName, session.AcceptedVersion);
Console.WriteLine(session.Send(check));
```

`FindWindow()` returns the first shell window. With several editors running, pass a function to `ShellChannel(Func<nint>)` that returns the shell window HWND for the intended process. The parameterless constructor looks up the first window on each send.

`Begin` first closes any previous session in the editor and returns `null` if no version is accepted. The `using` declaration closes the open session. For each command, `Ok` means the editor accepted it, `Rejected` means the call returned zero, and `NotRunning` means the window lookup returned a zero HWND.

To try an edit, append the following code to the program. Choose an unoccupied position in the test map. The `ShellCommands` methods format command strings and `session.Send` delivers them to Hammer.

```csharp
var origin = new System.Numerics.Vector3(0, 0, 64);
var create = session.Send(ShellCommands.EntityCreate("info_target", origin));
if (create == ShellResult.Ok)
{
    var rename = ShellCommands.EntitySetKeyValue("info_target", origin, "targetname", "shell_test_target");
    Console.WriteLine(session.Send(rename));
}
```

## Sending a command

1. Find the hidden window whose class and title are both `Worldcraft_ShellMessageWnd`. If several editors are running, match the window's process ID to the editor you want to edit.
2. Encode one command as a NUL-terminated byte string. Hammer-MCP uses the Windows system ANSI code page and formats numbers with a decimal point.
3. Set `COPYDATASTRUCT.dwData` to zero, `cbData` to the byte count including the NUL, and `lpData` to the buffer. Send `WM_COPYDATA` (`0x004A`) with `SendMessage`, `wParam = 0`, and a pointer to the structure in `lParam`. Keep the buffer valid until the call returns.
4. A nonzero result means the editor accepted the command. A zero result carries no diagnostic text. If no window was found, check that the selected build exposes the channel. Also check the sender and editor's Windows privilege levels if delivery fails.

[SDK sender][sdk-sender] · [Hammer-MCP sender](../src/src/HammerShell.Client/ShellChannel.cs) · [WM_COPYDATA][copydata] · [SendMessage and UIPI][sendmessage]

## Edit sessions

Open and save a copy of the map in Hammer. Use the file name without its path or `.vmf` extension and read `mapversion` from the VMF's `versioninfo` block. For a map named `shell_test.vmf` with `mapversion` 12, and a free spot at `0 0 64`, send these lines separately. Proceed with the entity commands after Hammer accepts `session_begin`.

```text
session_begin shell_test 12
entity_create info_target 0 0 64
entity_set_keyvalue info_target 0 0 64 "targetname" "shell_test_target"
entity_delete info_target 0 0 64
session_end
```

Close an opened session even if an edit fails. On Hammer++ TF2 build 8873 x64, a nested `session_begin` was rejected, a session left open survived the sender's exit, and saving with Ctrl+S during a session kept it open. In those tests, `map_check_version` succeeded only inside an open session. Closing the session leaves accepted edits in the editor. Save separately if you want to write the edits to disk.

Hammer-MCP sends `session_end` before opening its own session to clear a stale one. It tries the saved version, then version +1 and version −1. Those retries are client behavior. For a manual test, first save the map and try the version found in the file. Coordinate access with any other sender using the same editor. [Hammer-MCP session code](../src/src/HammerShell.Client/ShellSession.cs)

## Command reference

The public SDK sender formats the following commands. Availability and behavior in a given editor build can be checked with a saved test map.

| Command | Arguments |
| --- | --- |
| `session_begin` | `mapname mapversion` |
| `map_check_version` | `mapname mapversion` |
| `session_end` | |
| `entity_create`, `entity_delete` | `classname x y z` |
| `entity_set_keyvalue` | `classname x y z "key" "value"` |
| `entity_rotate_incremental` | `classname x y z pitch yaw roll` |
| `node_create` | `nodeclass id x y z` |
| `node_delete` | `id` |
| `nodelink_create`, `nodelink_delete` | `start_id end_id` |

Coordinates are in map units, and incremental rotations are in degrees. Entity commands identify an object by class and origin. If two entities share both, the command cannot select one by its VMF ID. Hammer-MCP rejects embedded double quotes in keys and values because its command format has no escaping for them. [SDK sender][sdk-sender] · [Hammer-MCP command formatting](../src/src/HammerShell.Client/ShellCommands.cs)

The channel returns an acknowledgment and provides no map or selection data. Hammer-MCP reads the saved VMF for file-based queries. The public command set supports entity and node edits. Creating brush geometry needs a separate workflow. Menu actions such as Save use a separate window message. In the Hammer++ live test, views caught up when focus returned to the editor.

## Editor support

The channel has been identified in the stock Source 1 builds examined by this project, including the Episode One, SDK 2013, and later branches. In the Hammer++ builds examined, it appears from build 8867 onward. Entity edits and sessions were exercised live on Hammer++ TF2 8873 x64. The examined GoldSrc editors, Strata Hammer, and Source 2 Hammer lack this channel. The registry's `shell.present` records per-build findings. For the running process, check for the window or use `hammer_status` in Hammer-MCP. [Build registry](../src/registry/hammers.json)

[sdk-sender]: https://github.com/ValveSoftware/source-sdk-2013/blob/3e957b70b167929f120360115d359d1ecb4c1699/src/public/editor_sendcommand.cpp
[sdk-game]: https://github.com/ValveSoftware/source-sdk-2013/blob/3e957b70b167929f120360115d359d1ecb4c1699/src/game/server/wcedit.cpp
[copydata]: https://learn.microsoft.com/en-us/windows/win32/dataxchg/wm-copydata
[sendmessage]: https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-sendmessage
