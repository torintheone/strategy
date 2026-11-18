# Памятка для Claude Code

«Politeconomy» — глобальный политико-экономический симулятор (Godot 4.7.2 .NET + C#/.NET 8). Полное ТЗ — `SPEC.md`, план оставшихся частей — `ROADMAP.md`.

## Как начинать сессию

- Команда пользователя обычно: «Выполни часть X.Y по ROADMAP.md». Прочитай эту памятку, нужную строку `ROADMAP.md` и **верхнюю** запись `CHANGELOG.md` (образец формата).
- `SPEC.md`, `MODEL.md`, `DECISIONS.md`, `CHANGELOG.md` большие — **не читать целиком**. Нужный раздел искать: `grep -n "^## " MODEL.md`, затем читать только его. Раздел ТЗ — `grep -n "^## " SPEC.md`.
- Перед работой посмотреть, как сделана похожая система (например, `src/Politeconomy.Sim/News/` и `Events/` — свежие образцы), и делать так же.
- Одна часть — одна сессия.

## Структура

```
src/Politeconomy.Sim/<Система>/   модель: <Система>Model.cs (логика), World<Система>.cs (состояние), Systems/<Система>System.cs (шаг)
src/Politeconomy.Sim/Engine/      Simulation, SimulationHost (фоновый поток), *Snapshot.cs — снимки для интерфейса
src/Politeconomy.Sim/Data/        записи data/*.json (*File.cs), DataValidator.<Система>.cs, GameData.cs, DataFiles.cs
src/Politeconomy.Sim/Text/        тексты: Fmt (числа по-русски), EventText, LawText…
src/Politeconomy.Sim.Tests/       xUnit, по файлу на систему (<Система>Tests.cs), TestData.Game — настоящие данные
src/Politeconomy.Tools/Program.cs консольные команды: validate, check, events, news, ai…
game/scripts/Main.cs              корень интерфейса, дымовой тест (SmokeStep), скриншоты (ScreenshotStep)
game/scripts/UI/                  экраны и окна (GlassPanel, UiTheme — стиль)
data/                             контент в JSON, коэффициенты — data/balance.json, описание полей — data/README.md
```

## Правила кода и данных

- `Politeconomy.Sim` без `using Godot`. Файлы короткие, одна система на файл.
- Контент и коэффициенты — в `data/*.json`, не в коде. Новый файл данных: запись в `*File.cs`, `[JsonSerializable]` в `DataFiles.cs`, свойство и `Read` в `GameData.cs`, копия в `DataTests.Copy`, проверка в `DataValidator.<Система>.cs`, описание в `data/README.md`.
- Новый раздел `balance.json`: класс `<Система>Balance`, поле в `BalanceFile`, свойство `<Система>Balance` в `GameData`, `_comment` в JSON.
- Детерминизм: только `DeterministicRandom`; у политики, событий — свои генераторы. Словари в сохраняемом состоянии — `SortedDictionary` с `StringComparer.Ordinal`.
- Новое состояние мира: свойство в `World`, поле в `SaveState`/`Capture`/`Restore`, `SaveGame.FormatVersion` + 1, `Setup` в `GameSetup.NewGame`, `Ensure` в `GameSetup.Resume`, системы в `GameSetup.Systems()`.
- Тексты для игрока — по-русски, числа через `Fmt` (неразрывные пробелы, «−»). В журнал — `world.Log(country, kind, text[, newsSource])`; запись станет новостью, если `kind` есть в `data/news.json` → `sources`.
- Политиков и партий из реального мира нет.

## Проверка

- Сборка: `dotnet build Politeconomy.sln -c Release`.
- Тесты **только своей части**: `dotnet test src/Politeconomy.Sim.Tests -c Release --filter "FullyQualifiedName~<Система>Tests|FullyQualifiedName~DataTests"`. Полный набор (~10 мин) и прогон на 50 лет гоняет CI — локально не повторять.
- Данные: `dotnet run --project src/Politeconomy.Tools -c Release -- validate`.
- Если установлен Godot: `dotnet build game/Politeconomy.Game.csproj`, затем `godot --headless --path game -- --smoke` (10 с, строка `SMOKE OK`). В дымовой тест (`Main.SmokeStep`) добавлять именованную проверку новой части, в `ScreenshotStep` — 1–3 кадра.
- Окружение облачной сессии: `.NET 8` ставится `apt-get install -y dotnet-sdk-8.0`; Godot 4.7.2 mono — архив с GitHub releases (как в `.github/workflows/build.yml`).

## Сдача части

1. Код, данные, тесты, проверка в дымовом тесте, кадры скриншотов.
2. `MODEL.md` — раздел системы (формулы, коэффициенты, что сейчас получается в прогоне); строка в таблице «Порядок систем», если есть новая система; версия формата в «Сохранениях».
3. `DECISIONS.md` — новые решения `D-NNN` (последний номер: `grep -o "D-[0-9]*" DECISIONS.md | tail -1`).
4. `CHANGELOG.md` — запись сверху: «Что появилось» и «Что проверить в игре» по шагам, без жаргона.
5. `README.md` — новые клавиши; строка «Сейчас готовы…».
6. `ROADMAP.md` — отметить часть сделанной.
7. Коммит «Этап X.Y: …», push в рабочую ветку, дождаться зелёного CI (GitHub Actions «Сборка» и «Скриншоты»).

## Общение с пользователем

Пользователь — не программист. Вопросы — о дизайне и вкусе, не о технике; неясную деталь решить самому и записать в `DECISIONS.md`. Итог — коротко: что появилось, что проверить в игре по шагам.
