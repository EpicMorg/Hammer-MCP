# История изменений

[English](CHANGELOG.md) · **Русский**

Все заметные изменения Hammer-MCP. Версии — по [SemVer](https://semver.org/lang/ru/); релизы `-alpha` — пре-релизы.

## [Unreleased]

### Добавлено
- **Переменные префабов vmap**: keyvalues, привязанные к переменным префаба, получают значения размещённого префаба — значения по умолчанию, группы выбора (например, `TowerLocation = middle_tier_3` задаёт имя и юнит башни) и переопределения; именованный префаб — цель I/O внешней карты (его мир пробрасывает входы). Башни Dota теперь получают настоящие имена. `fixupEntityNames` не применяется (правило переименования в данных не подтверждено).
- `validate_map` показывает outputs, записанные как обычные keyvalues (`outputsAsKeyValues`, заданные через shell-канал), и проверяет их цели и inputs; `get_entity` / `find_io` показывают их как outputs.
- Карты Source 2 кэшируются по пути, времени записи и размеру: инструменты больше не перечитывают большую `.vmap` с префабами на каждый вызов.
- Брашевые сущности (`trigger_*`, `func_*`) адресуются центром своих брашей: Hammer++ пишет его как их `origin` (проверено вживую: `set_keyvalue` и outputs на `trigger_multiple` / `func_brush`); для .vmf без него центр считается по плоскостям брашей (`center` в `list_entities` / `get_entity`).
- Strata проверен вживую (Portal: Revolution x64): сборка, игровой конфиг, FGD, `validate_map`, последовательности Run Map; подставляются `$postcompiler_exe` и `$bsp`, а `|all_source_engine_paths|` указывает на корень игры, когда exe игры лежит в `bin\win64`.
- Таблица Meta info: контекстное меню по ПКМ — Copy (ячейка), Copy row (строка), Copy all (всё).

## [0.4.0] - 2026-11-13

Source 2: несохранённое состояние Hammer через консоль разработчика, раскрытие префабов; игровой конфиг и автосейвы Strata.

### Добавлено
- **Strata (Portal: Revolution)**: игровой конфиг из `hammer/cfg/gameconfig.txt` (пути с `..\..`, как в поставке, нормализуются), папка автосейвов из `hammer/cfg/HammerConfig.vdf` (KV3) для `fromAutosave`; `revolution.fgd` читается чисто (563 класса).
- **Консоль разработчика Source 2** (`source2_console`): команды из белого списка через VConsole2 (TCP 127.0.0.1:29000, тот же канал, что у `vconsole2.exe`) — `hammer_map_files`, `hammer_force_autosave`, `hammer_prefab_tree`, `find`, `help`, `status`, …; ничего не внедряется. Проверено вживую на CS2 (`-vconsole` не нужен).
- **Префабы и инстансы vmap раскрываются**: `CMapPrefab` (другая `.vmap`, ищется в папке мода / аддона карты, затем в стоковом моде) и `CMapInstance` со своими трансформациями, вложенность до 6 уровней; их сущности получают id от 1000000 и keyvalues `prefab` / `prefab_node`. `dota.vmap` из Dota: 69 своих сущностей, 1734 вместе с 67 префабами.
- **Несохранённое состояние Source 2**: инструменты чтения с `fromAutosave=true` выполняют `hammer_force_autosave` в консоли разработчика и читают самый свежий автосейв из `content\_toolsautosave\hammer\<папка карты>` (префабы ищутся в настоящем content). Открытая карта берётся из `hammer_map_files`, если в заголовке окна её нет. Проверено: консоль в Workshop Tools работает без `-vconsole`.
- Вывод консоли разработчика без мусора: у сообщений PRNT 28 байт заголовка (канал, флаги, цвет, время) перед текстом, проверено на CS2.

## [0.3.0] - 2026-11-13

Карты и игровые данные Source 2 (только чтение), проверено вживую на CS2 Workshop Tools; стоковый Hammer проверен вживую на TF2; правила безопасности для игровых процессов.

### Добавлено
- **Карты Source 2**: `.vmap` (DMX, чтение через KeyValues2 / Datamodel.NET) — сущности в мировых координатах с keyvalues и outputs, скрытые узлы, сводка. На картах Source 2 работают `map_summary`, `list_entities`, `get_entity`, `find_io`, `validate_map`; мир — цель I/O (проброс через префабы). Префабы и инстансы пока не раскрываются.
- **FGD Source 2**: блоки метаданных класса (`{ entity_tool_name = ... }`, их показывает `fgd_class`), метаданные свойств `[ ]` / `{ }`, `@OverrideClass`, экранированные кавычки, include из `game/core/`; определения моделей / VData / helpinfo пропускаются. CS2 `csgo.fgd`: 469 классов, Dota 2 `dota.fgd`: 427, без предупреждений.
- **Игровые данные Source 2** без GameConfig.txt: по конфигу на каждый мод с `game/<mod>/<mod>.fgd`, выбор по папке `content/<mod>` открытой карты (`csgo_addons` → `csgo`).
- **Процессы Workshop Tools Source 2** (`cs2`, `dota2`, `hlvr`, … с `tools/hammer.dll`) показываются для Attach. Имя карты из заголовка Hammer ищется в `content/<mod>/maps` и `content/<mod>_addons/<аддон>/maps`.
- `hammer_status`: заголовок окна, командная строка процесса (флаг `insecure`) и для неизвестных сборок — sha256 DLL редактора для реестра.
- **Политика внедрения** для будущего режима Advanced (`InjectPolicy`): только отдельный exe Hammer известной сборки с адаптером; никогда в игровые процессы (VAC / античит), никогда в Source 2 и неизвестные сборки.
- README: раздел **Безопасность и VAC** (EN / RU).
- Реестр: `hammer.dll` CS2 от 2026-11-13.

### Изменено
- Для карты вне всех MapDir берётся игровой конфиг установки самого Hammer (единственный, чья папка игры лежит внутри неё и содержит gameinfo.txt, например `tf` для Hammer TF2), вместо требования `configName`.
- Тексты для Source 2 в окне и для агента: свой статус Attach («только чтение карты и файлов игры»), подсказки режимов, инструменты меню вежливо отказывают, инструкции агенту.
- Описания инструментов после живого теста стокового Hammer (TF2): `set_keyvalue` не попадает в историю Undo Hammer; `rotate_entity` поворачивает только сущности с моделью — для остальных задавайте keyvalue `angles`.
- Типы FGD выводятся без пробелов (`resource:vjs`); служебные ключи редактора Source 2 (`local.origin`, `useLocalOffset`, …) не считаются неизвестными keyvalues.

### Проверено вживую
- **CS2 Workshop Tools**: Attach к `cs2.exe`, прочитана и проверена `.vmap` в 69 МБ (формат 40) из аддона. Workshop Tools ведут себя как insecure-клиент (VAC-серверы закрыты).
- **Стоковый Hammer TF2 x86**: create / keyvalue / delete / save через shell-канал, outputs как keyvalues, `validate_map`, `check_assets` (gameinfo + VPK), `compile_sequences` из бинарного `CmdSeq.wc`.

## [0.2.0] - 2026-11-13

Чтение карт, игровых данных и конвейер компиляции; первые живые правки логики; намного больший реестр сборок.

### Добавлено
- **Знание карты**: `get_entity`, `find_io` (граф логики: кто кого и чем вызывает), `validate_map` (неизвестные классы, keyvalues и outputs, которых нет в FGD, outputs на несуществующие цели, inputs, которых нет у цели, значения с двойными кавычками внутри).
- **Игровые данные**: `game_configs` (игровые конфигурации Hammer / Hammer++), `fgd_class`, `fgd_search`. Терпимый парсер FGD: `@version` / `@ExtendClass` из Hammer++, типы `*studio` из Garry's Mod, переопределение классов и наследование в момент разбора, как в fgdlib.
- **Форматы**: VMF / VMX / VMM / `vmf_autosave` с outputs (разделители `,` или ESC) и скрытыми сущностями; RMF / MAP / JMF через Sledge.Formats; материалы VMT и игровые конфиги Hammer через ValveKeyValue.
- **Ассеты**: `check_assets` — материалы, модели, спрайты, декали, skybox сохранённой карты проверяются по путям поиска `gameinfo.txt` (`|gameinfo_path|`, `|all_source_engine_paths|`, `|appid_N|` через библиотеки Steam), папкам и каталогам VPK.
- **Компиляция**: `compile_sequences` (Run Map-последовательности из `CmdSeq.wc`, файлов Hammer++ и Strata, с подстановкой переменных), `compile_log` (стадии, утечки, отсутствующие материалы, ошибки моделей, лимиты), `leak_trace` (путь утечки из `.lin`), `run_compile` (только в режимах с правками; запуск игры и окно просмотра лога пропускаются), `bsp_entities` (entity lump скомпилированного `.bsp`, с outputs).
- **Автосейвы**: инструменты чтения принимают `fromAutosave`, чтобы брать самый свежий автосейв, если он новее сохранённой карты (`AutosaveDir` Hammer++, `Autosave Dir` стокового Hammer из реестра Windows).
- **Реестр сборок**: 179 известных сборок редакторов — все релизы Hammer++ 8846–8873 (все ветки), стоковый Hammer Source 1 из игр и SDK Valve (x86 и x64), моды (Portal Stories: Mel, Aperture Tag, Portal Reloaded, Black Mesa, …), Slammin' Hammer, Strata (Portal: Revolution), Source 2 (`tools/hammer.dll` из CS2 и Dota 2) и GoldSrc (Worldcraft 1.1–3.3, Hammer 3.4 / 3.5, Neon / Neo / CSM Hammer 3.5). Сборки Source 2 и GoldSrc распознаются (GoldSrc — по самому exe редактора); для живых правок по-прежнему нужен shell-канал.

### Изменено
- В заголовке главного окна показывается версия приложения; «О программе» и заголовок берут её из ресурса версии exe (в локальных сборках «О программе» показывало 0.1.0-alpha).
- `run_compile` / `compile_sequences` берут в кавычки подставленные аргументы с пробелами, как это делает Hammer (`-game "...\Bestiarium Project\bst"`), а `run_compile` не читает лог, оставшийся от прошлой компиляции.
- `validate_map` ищет цель output'а по targetname, затем по classname (как движок) и принимает классы, которые появляются только в игре (`player`, …).
- Outputs выводятся в инвариантной культуре (`delay 0.5`, а не `0,5`).
- Отсутствующий путь поиска `custom/*` больше не считается проблемой.

### Примечания
- Outputs, заданные через `set_keyvalue` (`OnTrigger` = `target,Input,param,delay,times`), Hammer хранит как обычные keyvalues: на вкладке Outputs их не видно, но vbsp записывает их в `.bsp` как настоящие outputs, и в игре они срабатывают (проверено через `bsp_entities`).
- Значение с двойными кавычками внутри (например, `echo "text"`) Hammer обрезает при загрузке, и следующее сохранение закрепляет порчу; теперь `validate_map` предупреждает о таких строках.

## [0.1.0-alpha] - 2026-11-12

Первый публичный пре-релиз.

### Добавлено
- MCP-сервер поверх Streamable HTTP (`/mcp`, порт по умолчанию 47800, Bearer-токен; обязателен, если сервер слушает не только localhost).
- Приложение в трее (WinForms): выбор и подключение (Attach) к запущенному Hammer, запуск / остановка сервера, режимы (Auto / Basic / Shell / Advanced), адрес, порт, токен, логи, окно «О программе».
- Живые правки через shell-канал Hammer (`Worldcraft_ShellMessageWnd`): `create_entity`, `set_keyvalue`, `rotate_entity`, `delete_entity` (отказ при неоднозначной адресации), `undo`, `redo`, `save_map`, `open_run_map_dialog`, `open_check_for_problems`.
- Чтение сохранённой карты: `hammer_status`, `map_summary`, `list_entities`.
- Встроенный реестр сборок (`registry/hammers.json`, переопределяется файлом рядом с exe).

[0.4.0]: https://github.com/EpicMorg/Hammer-MCP/compare/v0.3.0...v0.4.0
[0.3.0]: https://github.com/EpicMorg/Hammer-MCP/compare/v0.2.0...v0.3.0
[0.2.0]: https://github.com/EpicMorg/Hammer-MCP/compare/v0.1.0-alpha...v0.2.0
[0.1.0-alpha]: https://github.com/EpicMorg/Hammer-MCP/releases/tag/v0.1.0-alpha
