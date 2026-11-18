"""Собирает PORTRAITS.md: список портретов и иллюстраций событий с промптами для Grok.

Запуск: python tools/portraits_list.py  (перезаписывает PORTRAITS.md в корне репозитория).

Список детерминирован: одни и те же регионы и правила дают одни и те же имена файлов и промпты, поэтому файлы,
которые уже сгенерированы и загружены, не «съезжают» при пересборке. Регионы внешности — art/portrait_regions.json.
"""
import json
import pathlib

ROOT = pathlib.Path(__file__).resolve().parent.parent
REGIONS = json.loads((ROOT / "art" / "portrait_regions.json").read_text(encoding="utf-8"))["regions"]

STYLE = ("Semi-realistic digital painting, official waist-up portrait, soft studio lighting, plain neutral dark "
         "grey-blue background, muted natural colors, subtle painterly brushwork, vertical 3:4 composition, "
         "the person looks at the viewer. This is an invented, fictional person who does not resemble any real "
         "person. No text, no logos, no flags, no national emblems or real insignia.")

AGES = {
    "young": ("30–44", "in {p} late thirties"),
    "mid": ("45–59", "in {p} early fifties"),
    "old": ("60–75", "in {p} late sixties, with gray hair and visible wrinkles"),
}
GENDERS = {"m": ("муж.", "man"), "f": ("жен.", "woman")}
ROLES = {"pol": "политик", "mil": "военный", "tv": "ведущий", "sci": "учёный"}

LOOK_M = ["short neatly combed hair", "close-cropped hair", "a receding hairline", "a neatly trimmed beard",
          "rimless glasses", "thick-rimmed glasses", "slight stubble", "short hair with a side part",
          "a mustache", "clean-shaven, with thick eyebrows"]
LOOK_F = ["shoulder-length hair", "hair tied back in a neat bun", "a short bob haircut", "long straight hair",
          "wavy hair", "pearl earrings", "thin-framed glasses", "short hair", "a low ponytail", "a side-swept fringe"]
MOODS = ["a calm, confident expression", "a slight reserved smile", "a serious, composed expression",
         "a thoughtful expression", "a warm, approachable look"]
SUITS = ["charcoal", "navy", "dark gray", "black", "dark blue pinstripe", "graphite"]
TIES = ["a burgundy tie", "a dark blue tie", "a silver-gray tie", "a muted green tie", "an open collar, no tie"]

# Разнообразие внешности внутри региона, где она заметно разная.
ORIGINS = {
    "north_america_oceania": ["of European descent", "African American", "Hispanic", "of East Asian descent",
                              "of South Asian descent", "Indigenous"],
    "latin_america": ["mestizo", "of European descent", "Afro-Latin", "of Indigenous Andean descent",
                      "Afro-Caribbean"],
    "africa_east": ["East African", "Ethiopian", "Southern African", "Somali"],
    "africa_west": ["West African", "Central African", "Sahelian"],
    "middle_east": ["Arab", "Persian", "North African", "Levantine"],
    "southeast_asia": ["Malay", "Vietnamese", "Thai", "Filipino", "Indonesian"],
    "east_asia": ["Chinese", "Japanese", "Korean"],
    "south_asia": ["North Indian", "South Indian", "Pakistani", "Bangladeshi", "Sri Lankan"],
    "central_asia": ["Kazakh", "Uzbek", "Mongolian", "Kyrgyz"],
    "east_europe": ["Slavic", "Balkan", "Baltic", "Hungarian or Romanian"],
    "caucasus_turkey": ["Turkish", "Georgian", "Armenian", "Azerbaijani"],
    "south_europe": ["Italian", "Spanish", "Greek", "Portuguese"],
    "nw_europe": ["British or Irish", "German or Dutch", "Scandinavian", "French"],
    "pacific": ["Polynesian", "Melanesian", "Micronesian"],
}


def politician_attire(region, gender, k):
    """Одежда политика: деловой костюм, иногда — национальная одежда региона."""
    suit = SUITS[k % len(SUITS)]
    if gender == "m":
        default = f"a {suit} business suit with {TIES[k % len(TIES)]}"
        special = {
            "middle_east": "a traditional white thobe with a white-and-red checkered headdress",
            "south_asia": "a dark Nehru jacket over a white kurta",
            "africa_west": "a richly embroidered traditional robe in deep colors",
            "africa_east": "a patterned shirt under a dark jacket",
            "southeast_asia": "a long-sleeved batik shirt",
            "pacific": "a dark patterned island shirt",
            "central_asia": "a business suit and an embroidered traditional skullcap",
        }
    else:
        default = f"a tailored {suit} business suit with a simple blouse"
        special = {
            "middle_east": "an elegant dark business suit and a modest headscarf",
            "south_asia": "an elegant silk sari in a muted color",
            "africa_west": "a colorful traditional dress with a matching head wrap",
            "africa_east": "an elegant patterned dress and a light shawl",
            "southeast_asia": "an elegant embroidered kebaya-style blouse",
            "pacific": "a patterned island dress with a flower behind the ear",
            "east_asia": "a tailored jacket with a high collar",
        }
    return special[region] if region in special and k % 3 == 2 else default


def attire(role, region, gender, k):
    if role == "pol":
        return politician_attire(region, gender, k)
    if role == "mil":
        return "a dark dress military uniform with generic medals and plain shoulder boards, no national insignia"
    if role == "tv":
        return "a smart tailored blazer, polished television-presenter look, light studio makeup"
    return "a white lab coat over a shirt" if k % 2 == 0 else "a tweed jacket and a knitted vest, academic look"


def role_words(role, gender):
    noun = GENDERS[gender][1]
    return {
        "pol": f"politician, a {noun}",
        "mil": f"senior military officer, a {noun}",
        "tv": f"television news anchor, a {noun}",
        "sci": f"scientist, a {noun}",
    }[role]


def plan(region):
    """Какие портреты нужны региону: (роль, пол, возраст, число вариантов)."""
    small = region["id"] == "pacific"
    rows = []
    for age in AGES:
        rows.append(("pol", "m", age, 1 if small else 3))
    for age in AGES:
        rows.append(("pol", "f", age, 1 if small else 2))
    rows += [("mil", "m", "mid", 1), ("mil", "m", "old", 1), ("mil", "f", "mid", 1),
             ("tv", "f", "young", 1), ("tv", "f", "mid", 1), ("tv", "m", "mid", 1)]
    return rows


def portraits():
    out = []
    for r_index, region in enumerate(REGIONS):
        rows = plan(region)
        # Учёный: в чётных регионах — мужчина, в нечётных — женщина.
        rows.append(("sci", "m" if r_index % 2 == 0 else "f", "mid", 1))
        k = r_index
        for role, gender, age, count in rows:
            for n in range(1, count + 1):
                k += 1
                origins = ORIGINS.get(region["id"])
                origin = f"{origins[k % len(origins)]} " if origins else ""
                look = (LOOK_M if gender == "m" else LOOK_F)[k % 10]
                if age == "old" and gender == "m" and "hair" in look and "receding" not in look:
                    look = "short gray hair"
                prompt = (f"{STYLE} Subject: a fictional {origin}{role_words(role, gender)} from {region['prompt']}, "
                          f"{AGES[age][1].format(p='his' if gender == 'm' else 'her')}, with {look}, {MOODS[k % len(MOODS)]}, wearing {attire(role, region['id'], gender, k)}.")
                name = f"p_{region['id']}_{role}_{gender}_{age}_{n}.jpg"
                out.append((name, region, gender, age, role, prompt))
    return out


EVENT_STYLE = ("Semi-realistic digital painting, wide 16:9 editorial illustration, muted desaturated palette with one "
               "warm accent, cinematic lighting, painterly brushwork. Fictional place and people; no recognizable real "
               "people, landmarks, flags, logos or text.")

# ~50 крупных событий: id из data/events.json → сцена.
EVENTS = [
    ("earthquake", "a city street after a strong earthquake: cracked asphalt, collapsed facades, rescuers with flashlights in dust"),
    ("flood", "a flooded town with water up to the windows, people on rooftops, a rescue boat"),
    ("drought", "cracked dry farmland under a white-hot sky, a farmer looking at withered crops"),
    ("wildfires", "a forest fire at dusk, orange smoke over hills, firefighters and a water-bombing plane"),
    ("blackout", "a city skyline at night with whole districts dark, people with phone flashlights on a street"),
    ("new_infection", "a hospital corridor full of patients, doctors in protective suits and masks"),
    ("infection_second_wave", "an empty city square with masked passers-by and a closed cafe, gray rainy day"),
    ("cholera_outbreak", "a crowded poor neighborhood, medical workers handing out clean water, a field clinic tent"),
    ("miners_strike", "miners in helmets with lamps standing at a mine gate with handmade banners"),
    ("general_strike", "a huge crowd of workers marching down a wide boulevard, idle factory chimneys behind"),
    ("doctors_strike", "doctors in white coats standing on hospital steps with blank protest signs"),
    ("teachers_strike", "teachers with books and blank placards in front of a school, empty classroom windows"),
    ("farmers_blockade", "a line of tractors blocking a highway at dawn, farmers standing by a bonfire"),
    ("truckers_blockade", "heavy trucks parked across a motorway, drivers talking, a long traffic jam"),
    ("student_protests", "students with blank banners on university steps, police line in the distance"),
    ("opposition_rally", "a large evening rally on a city square, a speaker on a small stage, a sea of raised hands"),
    ("green_protests", "young people with face masks and blank green placards under a smoggy sky"),
    ("police_video", "a smartphone screen showing a blurred street scene, people watching it in shock"),
    ("oil_price_crash", "an empty oil terminal with idle tankers, a falling red chart on a trader's screen"),
    ("oil_windfall", "oil pump jacks at sunset and a busy port with tankers loading"),
    ("bank_failure", "a long queue of worried people outside a closed bank with shuttered doors"),
    ("housing_crash", "a half-finished apartment block with idle cranes and 'for sale' boards without text"),
    ("capital_flight", "a dim trading floor, screens with falling charts, a briefcase being carried out"),
    ("currency_crisis", "people crowding at a currency exchange window, a board with changing numbers, no readable text"),
    ("default_threshold", "a finance ministry meeting room at night, officials over thick folders and graphs"),
    ("food_price_spike", "a market stall with sparse produce and worried shoppers counting coins"),
    ("record_harvest", "golden wheat fields at harvest, combines working, full grain silos"),
    ("robotization", "a bright modern factory floor with robotic arms and a few workers watching"),
    ("ai_boom", "an open-plan office with glowing screens of abstract neural network visuals"),
    ("tech_breakthrough", "scientists celebrating in a laboratory around a glowing experimental device"),
    ("space_program", "a rocket on a launch pad at dawn with service towers and a distant crowd"),
    ("ev_transition", "a charging station with electric cars on a city street, a gas station closed behind"),
    ("cyberattack", "a dark server room with red warning lights and an engineer at a terminal"),
    ("data_leak", "rows of personal data cards scattering like papers in the wind in front of a server rack"),
    ("refugees", "a column of refugees with bags walking along a dusty road toward a border checkpoint"),
    ("border_incident", "a mountain border post with soldiers behind barriers, smoke in the distance"),
    ("alliance_offer", "two delegations shaking hands across a long table with neutral flags without symbols"),
    ("minister_scandal", "a crowd of reporters with microphones surrounding an official leaving a building"),
    ("offshore_leak", "stacks of leaked documents on a journalist's desk under a lamp, a laptop glowing"),
    ("coup_rumors", "armored vehicles parked near a government building at night, uneasy soldiers"),
    ("media_closure", "an empty television studio with the lights turned off and a sealed door"),
    ("term_limit_end", "an empty presidential desk in a grand office with an open constitution book"),
    ("nuclear_accident", "a nuclear power plant with a damaged reactor building and a plume of steam, evacuation buses"),
    ("dam_failure", "a broken dam with water rushing into a valley, a flooded village below"),
    ("oil_spill", "a black oil slick on a beach, volunteers in protective suits cleaning, oiled birds"),
    ("mine_disaster", "rescuers at a mine shaft entrance at night, families waiting behind a barrier"),
    ("chemical_plant_accident", "a chemical plant with a toxic yellow cloud, people being evacuated"),
    ("bridge_collapse", "a collapsed highway bridge over a river, emergency crews on the banks"),
    ("tsunami", "a giant wave approaching a coastal town, people running to higher ground"),
    ("locust_swarm", "a dark swarm of locusts over green fields, a farmer shielding his face"),
    ("heatwave", "a city under scorching sun, people sheltering in the shade, a thermometer-like heat haze"),
    ("winter_cold_snap", "a frozen city at night, frost on windows, people around a warming tent"),
    ("smog_crisis", "a city skyline barely visible through thick brown smog, pedestrians in masks"),
]


def write():
    rows = portraits()
    regions_table = "\n".join(
        f"| `{r['id']}` | {r['name']} | {len(r['countries'])} | {' '.join(r['countries'])} |" for r in REGIONS)
    portrait_table = "\n".join(
        f"| {i} | `{name}` | {region['name']} | {GENDERS[g][0]} | {AGES[a][0]} | {ROLES[role]} | {prompt} |"
        for i, (name, region, g, a, role, prompt) in enumerate(rows, 1))
    event_table = "\n".join(
        f"| {i} | `e_{eid}.jpg` | `{eid}` | {EVENT_STYLE} Scene: {scene}. |" for i, (eid, scene) in enumerate(EVENTS, 1))
    counts = {}
    for _, _, _, _, role, _ in rows:
        counts[role] = counts.get(role, 0) + 1
    text = f"""# Портреты и иллюстрации событий

Список картинок, которые нужно сгенерировать в Grok: **{len(rows)} портретов** вымышленных людей (политики — {counts['pol']}, военные — {counts['mil']}, телеведущие — {counts['tv']}, учёные — {counts['sci']}) и **{len(EVENTS)} иллюстраций** крупных событий. Файл собирает `tools/portraits_list.py` — руками его не править: имена файлов и промпты пересоберутся.

## Как это сделать (по шагам)

1. **Откройте Grok** (grok.com или приложение) и перейдите в режим картинок (Imagine / «Создать изображение»).
2. **Возьмите строку из таблицы ниже** — например, № 1. Скопируйте текст из колонки «Промпт» целиком (он на английском — так Grok лучше держит стиль) и вставьте в Grok.
3. **Если Grok предлагает формат**, выберите вертикальный 3:4 для портретов (или 2:3, 9:16 — скрипт обрежет) и горизонтальный 16:9 для иллюстраций событий. Если не предлагает — ничего страшного: скрипт обрежет сам.
4. **Выберите один вариант из тех, что дал Grok**: лицо крупно и не обрезано, человек по пояс, фон спокойный, нет надписей и флагов, стиль похож на уже выбранные портреты. Если все варианты не подходят — нажмите «ещё раз».
5. **Скачайте картинку и переименуйте файл точно как в колонке «Файл»** — например, `p_nw_europe_pol_m_young_1.jpg`. Расширение может быть любым (`.jpg`, `.png`, `.webp`) — главное, чтобы совпадало имя до точки. Без имени из списка скрипт не поймёт, кто это.
6. **Загрузите картинки в репозиторий**: GitHub → папка `art/incoming` → кнопка **Add file** → **Upload files** → перетащите файлы → внизу **Commit changes**. Можно частями: GitHub принимает до 100 файлов за раз. Не обязательно генерировать всё сразу.
7. **Когда загружено хотя бы 50 портретов**, напишите Claude: «Выполни вторую половину части 8.1 по ROADMAP.md». Он обработает картинки (обрежет, выровняет цвет, проставит теги), подключит их к персонажам и скажет, каких портретов не хватает. Там, где портрета нет, остаётся силуэт.

Советы:

- **Начните с 1–2 строк каждого региона** и посмотрите, как они выглядят вместе: если стиль «плывёт» (одни как фото, другие как мультфильм), выбирайте варианты, похожие на первые удачные. Стиль задан в начале каждого промпта, менять его не нужно.
- **Лица должны быть вымышленными.** Промпт об этом просит, но если вариант похож на известного человека — сгенерируйте заново.
- **Ничего страшного, если какие-то строки пропущены**: игра возьмёт портрет того же региона, пола и возраста, а если такого нет — силуэт.
- **В титрах будет строка** «Портреты и иллюстрации созданы с помощью Grok». Перед публикацией игры проверьте, что условия использования Grok разрешают это для ваших картинок.

## Регионы внешности

Персонаж получает портрет по стране: у каждой страны — регион внешности (`art/portrait_regions.json`), а в регионе — портрет нужного пола, возраста и роли.

| Регион | Название | Стран | Страны |
| --- | --- | --- | --- |
{regions_table}

Роли: **политик** — главы государств и министры; **военный** — министры обороны и силовики, генералы, хунта; **ведущий** — ведущий телевыпуска «Неделя»; **учёный** — для событий о науке. Возраст: 30–44, 45–59, 60–75 лет.

## Портреты

| № | Файл | Регион | Пол | Возраст | Роль | Промпт |
| --- | --- | --- | --- | --- | --- | --- |
{portrait_table}

## Иллюстрации событий

Горизонтальные картинки 16:9 к самым заметным событиям. В окне события они займут место цветной полосы над текстом.

| № | Файл | Событие | Промпт |
| --- | --- | --- | --- |
{event_table}

## Что делает скрипт обработки

`python tools/process_art.py` (его запускает Claude во второй половине части 8.1):

- находит в `art/incoming` картинки с именами из этого списка (регистр и расширение не важны) и сообщает о лишних и недостающих;
- портреты обрезает до 3:4 (лицо ближе к верхней трети, как на официальном портрете) и уменьшает до 600×800, иллюстрации — до 16:9 и 1280×720;
- выравнивает цвет: баланс белого, контраст, яркость и насыщенность — к общим значениям, плюс общий лёгкий «тон» игры, чтобы портреты из разных генераций смотрелись одной серией;
- сохраняет в `game/assets/portraits/` и `game/assets/events/` (формат WebP) и пишет `art/manifest.json` с тегами (регион, пол, возраст, роль) и тем, чего не хватает.
"""
    (ROOT / "PORTRAITS.md").write_text(text, encoding="utf-8")
    print(f"PORTRAITS.md: портретов {len(rows)} ({counts}), иллюстраций {len(EVENTS)}")


if __name__ == "__main__":
    write()
