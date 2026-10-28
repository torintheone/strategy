# Данные игры

Весь контент лежит здесь в открытых JSON-файлах. Игра читает их при запуске из папки `data` рядом с `Politeconomy.exe`, поэтому их можно менять — это основа для модов. После правки проверьте данные:

```
dotnet run --project src/Politeconomy.Tools -- validate
```

Файлы `countries.json`, `map/world.json` и `map/cities.json` собирает скрипт `tools/build_world.py`. Остальные файлы ведутся вручную.

## countries.json

Страны — члены ООН и их стартовые показатели.

| Поле | Что это |
| --- | --- |
| `id` | Код ISO 3166-1 alpha-3, например `RUS` |
| `iso2` | Двухбуквенный код |
| `name`, `short_name`, `official_name`, `name_en` | Название, короткое название для карты, официальное, английское |
| `capital`, `region`, `subregion` | Столица и регион мира по-русски |
| `area_km2`, `landlocked` | Площадь, нет ли выхода к морю |
| `income_group` | `low`, `lower_middle`, `upper_middle`, `high` |
| `income_source` | Откуда взята группа дохода |
| `stats` | Показатели: `{ "<id показателя>": { "value", "year", "source", "method"?, "n"? } }` |

`source`: `wb:<код Всемирного банка>`, `imf:<код МВФ>`, `fill` (медиана по похожим странам, `method` — как именно, `n` — сколько стран в выборке) или `derived` (вычислено, `method` — формула).

## indicators.json

Описание показателей: `id`, `name` (как называется в игре), `format`, `description` (текст подсказки), `unit` (необязательно).

Форматы: `people` (143,5 млн), `money_big` (2,56 трлн $), `money` (17 850 $), `percent` (8,7%), `percent_signed` (+2,2%), `percent_gdp` (17,9% ВВП), `years` (73,2 года), `number1`, `number2`.

## politics.json

`systems` — названия типов политических систем. `countries` — для каждой страны: `id`, `system` (тип), `note` (особенность или пометка «проверить»), `reviewed` (проверено ли человеком).

Типы: `presidential_republic`, `parliamentary_republic`, `semi_presidential`, `constitutional_monarchy`, `absolute_monarchy`, `one_party_state`, `military_regime`.

## scenario.json

`id`, `name`, `description`, `start_date` (ГГГГ-ММ-ДД), `seed` (число для генератора случайностей), `warmup_months` (сколько месяцев модель скрыто прогоняется перед стартом, с этапа 1), `player_country` (код страны или `null` — выбрать на карте).

## balance.json

Коэффициенты модели; смысл каждого — в `MODEL.md`. Сейчас: `time.days_per_second` — пять скоростей времени, игровых дней в секунду.

## map_modes.json

- `palettes` — цвета в формате `#rrggbb`: `political` (8 цветов политической карты), `sequential` (шкала от меньшего к большему), `diverging_negative` / `diverging_neutral` / `diverging_positive` (две стороны от нуля, от сильного к слабому и от слабого к сильному), `no_data`, `territory`.
- `modes` — режимы карты: `id`, `name`, `short_name` (подпись кнопки), `kind`, `indicator`, `thresholds` (пороги классов по возрастанию), `hotkey`.
  - `political` — каждая страна своим цветом, соседи различаются.
  - `bins` — классы по порогам, цвета из `sequential`. Классов на один больше, чем порогов.
  - `diverging` — классы по порогам вокруг нуля; число порогов чётное и вдвое больше длины каждого плеча палитры.
  - `politics`, `income` — категории, цвета из `politics_colors` и `income_groups`.

## map/world.json

Геометрия карты в проекции Equal Earth (ширина 8192, ось y вниз). `countries[]`: `id`, `label` (точка подписи `[x, y]`), `color` (номер цвета политической карты), `neighbors` (соседи по суше), `polygons[]` — `rings` (внешнее кольцо и дыры, координаты подряд `x0, y0, x1, y1, …`) и `triangles` (индексы вершин, по три на треугольник). `territories[]` — нейтральные территории с `name` и `note`.

## map/cities.json

`cities[]`: `name`, `country`, `x`, `y` (в координатах карты), `population`, `capital`, `rank` (важность по Natural Earth). В список входят столицы, города-миллионники и по три крупнейших города каждой страны.
