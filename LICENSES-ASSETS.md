# Источники данных, шрифты и лицензии

Всё, что не написано для проекта с нуля, перечислено здесь. Этот же список показывается на экране титров (появится на этапе 8).

## Данные

| Что | Источник | Лицензия | Где используется |
| --- | --- | --- | --- |
| Границы стран 1:50m, населённые пункты | [Natural Earth](https://www.naturalearthdata.com/), версия 5.1.2, [репозиторий проекта](https://github.com/nvkelso/natural-earth-vector) | Общественное достояние | `data/map/world.json`, `data/map/cities.json` |
| Население, ВВП, рост, инфляция, безработица, госдолг, налоги, торговля, структура экономики, демография, неравенство, военные расходы, доступ к электричеству, ВНД для групп дохода | Всемирный банк, [World Development Indicators](https://datacatalog.worldbank.org/search/dataset/0037712), через зеркало [datasets/world-development-indicators](https://github.com/datasets/world-development-indicators) | [CC BY 4.0](https://creativecommons.org/licenses/by/4.0/) | `data/countries.json` |
| Коды стран, членство в ООН, русские названия, регионы | [mledoze/countries](https://github.com/mledoze/countries) | [ODbL 1.0](https://opendatacommons.org/licenses/odbl/1-0/) | `data/countries.json` |
| Тип политической системы | Ручная классификация, черновик составлен Claude Code | — | `data/politics.json` |

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
