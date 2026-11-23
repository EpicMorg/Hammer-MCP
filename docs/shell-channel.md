# Hammer shell channel

[Русская версия](shell-channel.ru.md) · [Editor support matrix](editors.md)

The reference for Hammer's shell channel (`Worldcraft_ShellMessageWnd`, the mechanism behind `editor_sendcommand` in
the Source SDK) has moved to the repository of the C# library that implements it:

**→ [HammerShell: shell channel reference](https://github.com/EpicMorg/HammerShell/blob/master/docs/shell-channel.md)**

It covers where the channel comes from, how to send commands, edit sessions and the map version, every command and what
the channel cannot do. Hammer-MCP uses the library, [`EpicMorg.HammerShell.Client`](https://www.nuget.org/packages/EpicMorg.HammerShell.Client),
for its live edits. Which Hammer builds have the channel: [editor support matrix](editors.md).
