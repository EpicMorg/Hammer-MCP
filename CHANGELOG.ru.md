# История изменений

[English](CHANGELOG.md) · **Русский**

Все заметные изменения Hammer-MCP. Версии — по [SemVer](https://semver.org/lang/ru/); релизы `-alpha` — пре-релизы.

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

[0.2.0]: https://github.com/EpicMorg/Hammer-MCP/compare/v0.1.0-alpha...v0.2.0
[0.1.0-alpha]: https://github.com/EpicMorg/Hammer-MCP/releases/tag/v0.1.0-alpha
