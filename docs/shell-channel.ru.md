# Shell-канал Hammer

[English version](shell-channel.md)

Shell-канал позволяет программе под Windows отправлять команды редактирования в запущенный Hammer для Source 1. В публичном Source SDK Valve есть код отправителя, которым пользуется игровой режим редактирования карт. Hammer-MCP отправляет то же оконное сообщение из отдельного процесса, чтобы менять открытую карту. [Отправитель в SDK][sdk-sender] · [Игровой код SDK][sdk-game]

## API HammerShell.Client для C#

Для консольного приложения .NET 10 под Windows подключите проект библиотеки. Команда предполагает, что MyTool.csproj лежит в корне репозитория.

```sh
dotnet add MyTool.csproj reference src/src/HammerShell.Client/HammerShell.Client.csproj
```

Откройте и сохраните тестовую карту в одном экземпляре Hammer. В примере замените `shell_test` и `12` именем карты без расширения и значением `versioninfo.mapversion`. Этот фрагмент открывает сессию, проверяет версию и выводит ответ редактора.

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

`FindWindow()` возвращает первое окно канала. Если редакторов несколько, передайте в `ShellChannel(Func<nint>)` функцию, которая возвращает HWND окна канала нужного процесса. Конструктор без аргументов повторяет поиск первого окна при каждой отправке.

`Begin` сначала закрывает предыдущую сессию редактора и возвращает `null`, если ни одна версия не принята. `using` закрывает открытую сессию. Для команды `Ok` означает, что редактор её принял, `Rejected` — вызов вернул ноль, `NotRunning` — функция поиска вернула нулевой HWND.

Для примера правки добавьте следующий фрагмент в конец программы. Выберите свободную точку в тестовой карте. Методы `ShellCommands` формируют строки команд, а `session.Send` отправляет их редактору.

```csharp
var origin = new System.Numerics.Vector3(0, 0, 64);
var create = session.Send(ShellCommands.EntityCreate("info_target", origin));
if (create == ShellResult.Ok)
{
    var rename = ShellCommands.EntitySetKeyValue("info_target", origin, "targetname", "shell_test_target");
    Console.WriteLine(session.Send(rename));
}
```

## Отправка команды

1. Найдите скрытое окно с классом и заголовком `Worldcraft_ShellMessageWnd`. Если открыто несколько редакторов, сверьте PID окна с процессом нужного Hammer.
2. Закодируйте одну команду в байтовую строку с NUL в конце. Hammer-MCP использует системную ANSI-кодировку Windows и форматирует числа с десятичной точкой.
3. Укажите в `COPYDATASTRUCT.dwData` ноль, в `cbData` — длину в байтах вместе с NUL, в `lpData` — указатель на буфер. Отправьте `WM_COPYDATA` (`0x004A`) вызовом `SendMessage` с `wParam = 0` и указателем на структуру в `lParam`. Буфер должен оставаться действительным до возврата из вызова.
4. Ненулевой результат означает, что редактор принял команду. Нулевой результат приходит без текста ошибки. Если окно не найдено, проверьте, есть ли канал в выбранной сборке. При проблемах с доставкой также проверьте уровни привилегий отправителя и Hammer в Windows.

[Отправитель в SDK][sdk-sender] · [Отправитель Hammer-MCP](../src/src/HammerShell.Client/ShellChannel.cs) · [WM_COPYDATA][copydata] · [SendMessage и UIPI][sendmessage]

## Сессия редактирования

Откройте копию карты в Hammer и сохраните её. Возьмите имя файла без пути и расширения `.vmf`, а `mapversion` прочитайте из блока `versioninfo` в VMF. Для карты `shell_test.vmf` с `mapversion` 12 и свободной точкой `0 0 64` отправьте строки по одной. Выполняйте команды над сущностью после того, как Hammer примет `session_begin`.

```text
session_begin shell_test 12
entity_create info_target 0 0 64
entity_set_keyvalue info_target 0 0 64 "targetname" "shell_test_target"
entity_delete info_target 0 0 64
session_end
```

Закрывайте открытую сессию даже при отказе отдельной команды. На Hammer++ TF2 8873 x64 вложенный `session_begin` отклонялся, незакрытая сессия переживала выход отправителя, а сохранение через Ctrl+S её не прерывало. В этих тестах `map_check_version` срабатывал только внутри открытой сессии. После закрытия сессии принятые правки остаются в редакторе. Чтобы записать их на диск, сохраните карту отдельно.

Hammer-MCP перед началом своей сессии отправляет `session_end`, чтобы закрыть зависшую сессию. Он пробует сохранённую версию, затем версию +1 и версию −1. Этот перебор реализован на стороне клиента. Для ручного теста сначала сохраните карту и попробуйте версию из файла. Если к тому же экземпляру Hammer подключён другой отправитель, согласуйте работу с ним. [Код сессии Hammer-MCP](../src/src/HammerShell.Client/ShellSession.cs)

## Справочник команд

Публичный отправитель SDK формирует команды ниже. Доступность и поведение в конкретной сборке можно проверить на сохранённой тестовой карте.

| Команда | Аргументы |
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

Координаты задаются в единицах карты, приращения углов — в градусах. Команды над сущностью находят её по классу и координатам origin. Если эти поля совпадают у двух сущностей, выбрать одну из них по ID из VMF через канал нельзя. Hammer-MCP отклоняет двойные кавычки внутри ключа и значения, поскольку формат команды не предусматривает их экранирование. [Отправитель в SDK][sdk-sender] · [Форматирование команд в Hammer-MCP](../src/src/HammerShell.Client/ShellCommands.cs)

Канал возвращает признак приёма команды. Для чтения карты с диска Hammer-MCP использует сохранённый VMF. Публичный набор команд поддерживает правки сущностей и узлов. Для создания брашей нужен отдельный способ. Команды меню, включая сохранение, отправляются другим оконным сообщением. В живом тесте Hammer++ виды обновлялись после возврата фокуса в редактор.

## Поддержка в редакторах

Канал обнаружен в изученных стоковых сборках Hammer для Source 1, включая ветки Episode One, SDK 2013 и более поздние. Среди изученных сборок Hammer++ он появляется начиная с 8867. Сессии и правки сущностей проверены вживую на Hammer++ TF2 8873 x64. В изученных редакторах GoldSrc, Strata Hammer и Source 2 Hammer канала нет. Результаты по конкретным сборкам записаны в `shell.present` в реестре. Для запущенного редактора проверьте окно канала или вызовите `hammer_status` в Hammer-MCP. [Реестр сборок](../src/registry/hammers.json)

[sdk-sender]: https://github.com/ValveSoftware/source-sdk-2013/blob/3e957b70b167929f120360115d359d1ecb4c1699/src/public/editor_sendcommand.cpp
[sdk-game]: https://github.com/ValveSoftware/source-sdk-2013/blob/3e957b70b167929f120360115d359d1ecb4c1699/src/game/server/wcedit.cpp
[copydata]: https://learn.microsoft.com/en-us/windows/win32/dataxchg/wm-copydata
[sendmessage]: https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-sendmessage
