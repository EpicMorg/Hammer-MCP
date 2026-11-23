# Shell-канал Hammer

[English version](shell-channel.md) · [Поддержка редакторов](editors.ru.md)

Справочник по shell-каналу Hammer (`Worldcraft_ShellMessageWnd`, механизм, на котором построен `editor_sendcommand` в
Source SDK) переехал в репозиторий библиотеки для C#, которая этот канал реализует:

**→ [HammerShell: справочник по shell-каналу](https://github.com/EpicMorg/HammerShell/blob/master/docs/shell-channel.ru.md)**

Там описано, откуда канал взялся, как отправлять команды, сессии и версия карты, все команды и чего канал не умеет.
Hammer-MCP использует эту библиотеку, [`EpicMorg.HammerShell.Client`](https://www.nuget.org/packages/EpicMorg.HammerShell.Client),
для правок вживую. В каких сборках Hammer есть канал — в [таблице поддержки редакторов](editors.ru.md).
