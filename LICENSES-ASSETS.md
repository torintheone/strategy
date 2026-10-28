# Источники данных, шрифты и лицензии

Всё, что не написано для проекта с нуля, перечислено здесь. Этот же список показывается на экране титров (появится на этапе 8).

## Данные

| Что | Источник | Лицензия | Где используется |
| --- | --- | --- | --- |
| Границы стран и регионов 1:10m, населённые пункты | [Natural Earth](https://www.naturalearthdata.com/), версия 5.1.2, [репозиторий проекта](https://github.com/nvkelso/natural-earth-vector) | Общественное достояние | `data/map/world.json`, `data/regions.json` |
| Население, ВВП, рост, инфляция, безработица, налоги, торговля, структура экономики, демография, неравенство, военные расходы, доступ к электричеству, ВНД для групп дохода | Всемирный банк, [World Development Indicators](https://datacatalog.worldbank.org/search/dataset/0037712), API Всемирного банка | [CC BY 4.0](https://creativecommons.org/licenses/by/4.0/) | `data/countries.json` |
| Госдолг, сальдо, доходы и расходы бюджета, текущий счёт; показатели Тайваня | МВФ, [World Economic Outlook](https://www.imf.org/en/Publications/WEO), DataMapper API | [Условия МВФ](https://www.imf.org/external/terms.htm): свободное использование с указанием источника | `data/countries.json` |
| Население регионов, города от 15 тыс. жителей, русские названия | [GeoNames](https://www.geonames.org/) | [CC BY 4.0](https://creativecommons.org/licenses/by/4.0/) | `data/regions.json`, `data/map/cities.json` |
| Коды стран, членство в ООН, русские названия, регионы | [mledoze/countries](https://github.com/mledoze/countries) | [ODbL 1.0](https://opendatacommons.org/licenses/odbl/1-0/) | `data/countries.json` |
| Тип политической системы | Ручная классификация, черновик составлен Claude Code | — | `data/politics.json` |
| Упрощение границ | [mapshaper](https://github.com/mbloch/mapshaper) (инструмент сборки, в игру не входит) | MPL 2.0 | `tools/` |

Если в источниках нет значения для страны, игра подставляет медиану по похожим странам. Каждое такое место помечено в `data/countries.json` (`"source": "fill"`), а в игре — значком «≈».

## Шрифты

| Шрифт | Автор | Лицензия | Файл |
| --- | --- | --- | --- |
| IBM Plex Sans | IBM, через [Google Fonts](https://github.com/google/fonts/tree/main/ofl/ibmplexsans) | SIL Open Font License 1.1 | `game/assets/fonts/IBMPlexSans-Variable.ttf`, текст лицензии — `OFL-IBMPlexSans.txt` |
| PT Serif | ParaType, через [Google Fonts](https://github.com/google/fonts/tree/main/ofl/ptserif) | SIL Open Font License 1.1 | `game/assets/fonts/PTSerif-*.ttf`, текст лицензии — `OFL-PTSerif.txt` |

## Движок

[Godot Engine](https://godotengine.org/) 4.7 — лицензия MIT.

## Пока не используется

Портреты и иллюстрации (будут созданы с помощью Grok), озвучка, музыка и звуки появятся на этапе 8 и будут добавлены в этот список.
