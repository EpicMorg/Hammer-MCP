# Shell-канал Hammer

[English version](shell-channel.md) · [Поддержка редакторов](editors.ru.md)

В Hammer для Source 1 встроен небольшой пульт управления: скрытое окно, которое принимает текстовые команды от
других программ и применяет их к открытой карте. Через него можно создавать, менять и удалять сущности и править
AI-ноды. Valve использует его, чтобы запущенная игра могла передавать изменения обратно в редактор. Hammer-MCP
пользуется тем же каналом, чтобы агент мог вносить небольшие правки в открытую в Hammer карту без патчей редактора.

## Откуда он

- **Отправляющая сторона — открытый код Valve.** Во всех публичных Source SDK начиная с 2006 года (Episode One,
  Orange Box, Alien Swarm, 2013) есть `public/editor_sendcommand.cpp`: он находит окно и отправляет команды
  ([исходник в SDK 2013][sdk-sender]).
- **Им пользуется игра.** В `game/server/wcedit.cpp` ([исходник][sdk-game]) реализованы консольные команды `map_edit`
  и `hammer_update_entity`: они передают позиции и углы сущностей из запущенной игры в Hammer. Код AI-сети
  (`ai_networkmanager.cpp`, `ai_dynamiclink.cpp`) так же отправляет правки нод.
- **Сторона Hammer не публиковалась.** Обработчика команд нет ни в одном публичном SDK, поэтому всё, что ниже сказано
  о реакции Hammer, взято из кода отправителя в SDK и из проверок на реальных сборках.
- В старых ветках движка (2006/2007 и до Left 4 Dead 2 и Portal 2) лаунчер умеет загружать Hammer прямо в процесс игры
  по ключу `-edit`. В текущем Left 4 Dead 2 `hammer_dll.dll` действительно загружается, но до рабочего окна уже не
  доходит. В ветке 2013 этот путь убрали, а shell-канал в отдельном Hammer остался.

## Как отправить команду

1. Найдите окно, у которого и класс, и заголовок — `Worldcraft_ShellMessageWnd` (`FindWindow`). Если запущено
   несколько редакторов, переберите окна и выберите принадлежащее нужному процессу.
2. Закодируйте одну команду как байтовую строку с завершающим нулём в системной ANSI-кодировке. Числа пишутся через
   точку.
3. Отправьте её через `SendMessage(hwnd, WM_COPYDATA, 0, &cds)`, где `cds.dwData = 0`, `cds.cbData` — число байт
   вместе с нулём, `cds.lpData` — указатель на байты.
4. Ненулевой результат значит, что Hammer принял команду, ноль — что отклонил. Ни текста ошибки, ни данных в ответе
   нет.

`SendMessage` ждёт, пока Hammer обработает команду. Отправитель должен работать с тем же или более высоким уровнем
целостности Windows, что и Hammer (UIPI), иначе сообщение отбрасывается.

## Сессии и версия карты

Правки принимаются только внутри сессии, привязанной к открытой карте:

```text
session_begin <имя_карты> <mapversion>
  ... команды правки ...
session_end
```

- `имя_карты` — имя VMF без пути и расширения, `mapversion` — значение `versioninfo.mapversion` открытой карты. Если
  они не совпадают, Hammer отклоняет сессию, поэтому после открытия карту стоит один раз сохранить и прочитать версию
  из файла.
- Версия в памяти Hammer может уйти вперёд файла: после правок через канал следующей сессии может понадобиться
  `mapversion + 1`. Hammer-MCP пробует сохранённую версию, затем +1 и −1.
- Открытой может быть только одна сессия, второй `session_begin` отклоняется. Сессия, которую оставил упавший
  отправитель, остаётся открытой, поэтому клиенту стоит отправить `session_end` перед своим `session_begin`.
- `map_check_version <имя_карты> <mapversion>` проверяет привязку; в наших тестах она срабатывает только внутри
  сессии.
- Закрытие сессии не сохраняет карту. Сохранение по Ctrl+S во время сессии оставляет сессию открытой.

## Команды

| Команда | Аргументы | Что делает |
| --- | --- | --- |
| `session_begin` | `имя_карты mapversion` | открыть сессию правки открытой карты |
| `session_end` | | закрыть её |
| `map_check_version` | `имя_карты mapversion` | проверить, что сессия соответствует карте |
| `entity_create` | `класс x y z` | создать точечную сущность |
| `entity_delete` | `класс x y z` | удалить сущность этого класса в этой точке |
| `entity_set_keyvalue` | `класс x y z "ключ" "значение"` | задать keyvalue (в том числе у браш-сущностей, по центру их брашей) |
| `entity_rotate_incremental` | `класс x y z pitch yaw roll` | повернуть на заданные углы, в градусах |
| `node_create` | `класс_ноды id x y z` | создать AI-ноду |
| `node_delete` | `id` | удалить AI-ноду |
| `nodelink_create`, `nodelink_delete` | `id_начала id_конца` | связать или разъединить две AI-ноды |

Сущность ищется по классу и origin. Две сущности одного класса в одной точке различить нельзя. Ключи и значения
берутся в кавычки без экранирования, поэтому `"` в них быть не должно.

## Чего канал не умеет

- Ничего не читает: ни данных карты, ни выделения, ни камеры. Hammer-MCP читает сохранённый VMF, автосохранения Hammer,
  а с необязательным адаптером — выделение и камеру.
- Не создаёт и не меняет браши, не двигает сущности.
- Outputs, заданные как keyvalues, работают в игре, но не видны на вкладке Outputs.
- Undo: создание, удаление и поворот сущностей отменяются в Hammer, изменения keyvalues в историю Undo не попадают.
- Стоковый Hammer поворачивает только сущности с моделью; для остальных задавайте `angles`.
- Виды Hammer не перерисовываются, пока редактор снова не получит фокус. Hammer++ не помечает карту как изменённую.
- Пункты меню вроде Save или Run Map — не команды канала, а обычные сообщения `WM_COMMAND` главному окну.

## В каких редакторах он есть

Во всех изученных стоковых Hammer для Source 1 (Episode One 2007 и новее, x86 и x64), в Hammer++ начиная со сборки
8867, в Source Level Editor, Slammin' Hammer, Hammer из Black Mesa и Nuclear Dawn. Его нет в Hammer++ до 8866, в
Strata Hammer, Hammer для Source 2, редакторах GoldSrc и J.A.C.K. Подробно по сборкам — в
[таблице поддержки](editors.ru.md) и в поле `shell.present` [реестра сборок](../src/registry/hammers.json).

## Использование из C#

`HammerShell.Client` — код канала из Hammer-MCP в виде отдельной .NET-библиотеки (только Windows): поиск окна, сессии
с перебором версий, описанным выше, и форматирование команд.

```csharp
using HammerShell.Client;

var channel = new ShellChannel();                         // первое окно Worldcraft_ShellMessageWnd при каждой отправке
using var session = ShellSession.Begin(channel, "my_map", 12);  // имя карты и сохранённый mapversion
if (session is null) return;                              // ни одна версия не подошла

var at = new System.Numerics.Vector3(0, 0, 64);
if (session.Send(ShellCommands.EntityCreate("info_target", at)) == ShellResult.Ok)
    session.Send(ShellCommands.EntitySetKeyValue("info_target", at, "targetname", "from_shell"));
```

`Send` возвращает `Ok`, `Rejected` (Hammer вернул ноль) или `NotRunning` (окна нет). Если запущено несколько
редакторов, передайте в `ShellChannel` функцию, которая возвращает окно нужного процесса. Исходники:
[ShellChannel](../src/src/HammerShell.Client/ShellChannel.cs),
[ShellSession](../src/src/HammerShell.Client/ShellSession.cs),
[ShellCommands](../src/src/HammerShell.Client/ShellCommands.cs).

[sdk-sender]: https://github.com/ValveSoftware/source-sdk-2013/blob/master/src/public/editor_sendcommand.cpp#L188
[sdk-game]: https://github.com/ValveSoftware/source-sdk-2013/blob/master/src/game/server/wcedit.cpp
