#!/usr/bin/env python3
"""Сборка данных мира для Politeconomy.

Скачивает открытые источники один раз (в tools/.cache), собирает из них
data/countries.json, data/map/world.json и data/map/cities.json.
Игре интернет не нужен: результат лежит в репозитории.

Запуск:  python tools/build_world.py
Повторная загрузка источников:  python tools/build_world.py --refresh
"""

from __future__ import annotations

import argparse
import csv
import datetime as dt
import io
import json
import math
import pathlib
import statistics
import urllib.request

import mapbox_earcut as earcut
import numpy as np

ROOT = pathlib.Path(__file__).resolve().parent.parent
CACHE = ROOT / "tools" / ".cache"
DATA = ROOT / "data"

# Natural Earth — официальный репозиторий проекта на GitHub, версия 5.1.2.
NE_BASE = "https://raw.githubusercontent.com/nvkelso/natural-earth-vector/v5.1.2/geojson/"
NE_COUNTRIES = NE_BASE + "ne_50m_admin_0_countries.geojson"
NE_PLACES = NE_BASE + "ne_50m_populated_places.geojson"
# Справочник стран: коды ISO, членство в ООН, русские названия, регионы (ODbL).
COUNTRIES_REF = "https://raw.githubusercontent.com/mledoze/countries/master/countries.json"
# Показатели Всемирного банка (WDI), зеркало datahub.io с данными API Всемирного банка.
WDI_URL = "https://raw.githubusercontent.com/datasets/world-development-indicators/main/indicators/{code}/data.csv"

# Госдолг МВФ (World Economic Outlook). Если сайт МВФ недоступен, долг берётся из данных Всемирного банка.
IMF_DEBT_URL = "https://www.imf.org/external/datamapper/api/v1/GGXWDG_NGDP"

LATEST_YEAR = 2025

# key, код WDI (или список кодов по приоритету), самый ранний допустимый год, способ заполнения пропусков
INDICATORS = [
    ("population", ["SP.POP.TOTL"], 2015, "required"),
    ("gdp_usd", ["NY.GDP.MKTP.CD"], 2015, "derived"),
    ("gdp_per_capita_usd", ["NY.GDP.PCAP.CD"], 2015, "median"),
    ("gdp_ppp_per_capita", ["NY.GDP.PCAP.PP.CD"], 2015, "median"),
    ("gdp_growth_pct", ["NY.GDP.MKTP.KD.ZG"], 2020, "median"),
    ("inflation_pct", ["FP.CPI.TOTL.ZG", "NY.GDP.DEFL.KD.ZG"], 2020, "median"),
    ("unemployment_pct", ["SL.UEM.TOTL.ZS"], 2020, "median"),
    ("gov_debt_pct_gdp", ["GC.DOD.TOTL.GD.ZS"], 2015, "median"),
    ("tax_revenue_pct_gdp", ["GC.TAX.TOTL.GD.ZS"], 2015, "median"),
    ("exports_pct_gdp", ["NE.EXP.GNFS.ZS"], 2015, "median"),
    ("imports_pct_gdp", ["NE.IMP.GNFS.ZS"], 2015, "median"),
    ("agriculture_pct_gdp", ["NV.AGR.TOTL.ZS"], 2015, "median"),
    ("industry_pct_gdp", ["NV.IND.TOTL.ZS"], 2015, "median"),
    ("life_expectancy", ["SP.DYN.LE00.IN"], 2015, "median"),
    ("fertility_rate", ["SP.DYN.TFRT.IN"], 2015, "median"),
    ("urban_pct", ["SP.URB.TOTL.IN.ZS"], 2015, "median"),
    ("age_0_14_pct", ["SP.POP.0014.TO.ZS"], 2015, "median"),
    ("age_65_plus_pct", ["SP.POP.65UP.TO.ZS"], 2015, "median"),
    ("labor_force", ["SL.TLF.TOTL.IN"], 2015, "derived"),
    ("military_pct_gdp", ["MS.MIL.XPND.GD.ZS"], 2015, "median"),
    ("electricity_access_pct", ["EG.ELC.ACCS.ZS"], 2015, "median"),
    ("gini", ["SI.POV.GINI"], 2005, "median"),
]
GNI_ATLAS = "NY.GNP.PCAP.CD"

# Пороги групп дохода Всемирного банка на 2026 финансовый год (ВНД на душу за 2024 год, метод Атласа, долл. США).
INCOME_YEAR = 2024
INCOME_THRESHOLDS = [(1135, "low"), (4495, "lower_middle"), (13935, "upper_middle")]
NE_INCOME = {
    "1. High income: OECD": "high",
    "2. High income: nonOECD": "high",
    "3. Upper middle income": "upper_middle",
    "4. Lower middle income": "lower_middle",
    "5. Low income": "low",
}

REGION_RU = {
    "Africa": "Африка",
    "Americas": "Америка",
    "Asia": "Азия",
    "Europe": "Европа",
    "Oceania": "Океания",
}
SUBREGION_RU = {
    "Australia and New Zealand": "Австралия и Новая Зеландия",
    "Caribbean": "Карибский бассейн",
    "Central America": "Центральная Америка",
    "Central Asia": "Центральная Азия",
    "Central Europe": "Центральная Европа",
    "Eastern Africa": "Восточная Африка",
    "Eastern Asia": "Восточная Азия",
    "Eastern Europe": "Восточная Европа",
    "Melanesia": "Меланезия",
    "Micronesia": "Микронезия",
    "Middle Africa": "Центральная Африка",
    "North America": "Северная Америка",
    "Northern Africa": "Северная Африка",
    "Northern Europe": "Северная Европа",
    "Polynesia": "Полинезия",
    "South America": "Южная Америка",
    "South-Eastern Asia": "Юго-Восточная Азия",
    "Southeast Europe": "Юго-Восточная Европа",
    "Southern Africa": "Южная Африка",
    "Southern Asia": "Южная Азия",
    "Southern Europe": "Южная Европа",
    "Western Africa": "Западная Африка",
    "Western Asia": "Западная Азия",
    "Western Europe": "Западная Европа",
}

# Короткие названия для карты и интерфейса там, где обычное слишком длинное.
SHORT_NAMES = {
    "USA": "США",
    "ARE": "ОАЭ",
    "CAF": "ЦАР",
    "COD": "ДР Конго",
    "COG": "Конго",
    "DOM": "Доминикана",
    "BIH": "Босния и Герцеговина",
    "GNQ": "Экв. Гвинея",
    "PNG": "Папуа — Новая Гвинея",
    "VCT": "Сент-Винсент",
    "KNA": "Сент-Китс и Невис",
    "STP": "Сан-Томе и Принсипи",
    "FSM": "Микронезия",
    "MHL": "Маршалловы Острова",
}
NAME_FIXES = {
    "USA": "США",
    "PRK": "КНДР",
}
OFFICIAL_FIXES = {
    "PRK": "Корейская Народно-Демократическая Республика",
    "TLS": "Демократическая Республика Тимор-Лешти",
}

CAPITAL_FIXES = {
    "NRU": "Ярен",
    "SSD": "Джуба",
}
NEUTRAL_NAMES = {
    "KOS": "Косово",
    "CYN": "Северный Кипр",
    "PSX": "Палестина",
}

# Территории, которые не являются членами ООН: на карте нейтральные, в симуляции не участвуют.
NEUTRAL = {
    "TWN": "Тайвань — статус оспаривается",
    "KOS": "Косово — частично признанное государство",
    "PSX": "Палестина — государство-наблюдатель ООН",
    "SAH": "Западная Сахара — спорная территория",
    "SOL": "Сомалиленд — непризнанное государство",
    "CYN": "Северный Кипр — непризнанное государство",
    "KAS": "Ледник Сиачен — спорная территория",
    "VAT": "Ватикан — государство-наблюдатель ООН",
}
SKIP = {"ATA"}  # Антарктида не рисуется

MAP_WIDTH = 8192.0
POLITICAL_COLORS = 8


# ---------- загрузка ----------

def fetch(url: str, name: str, refresh: bool, timeout: int = 120) -> bytes:
    CACHE.mkdir(parents=True, exist_ok=True)
    path = CACHE / name
    if path.exists() and not refresh:
        return path.read_bytes()
    print(f"  скачиваю {url}")
    with urllib.request.urlopen(url, timeout=timeout) as r:
        data = r.read()
    path.write_bytes(data)
    return data


def load_wdi(code: str, refresh: bool) -> dict[str, dict[int, float]]:
    raw = fetch(WDI_URL.format(code=code.lower()), f"wdi_{code}.csv", refresh)
    out: dict[str, dict[int, float]] = {}
    for row in csv.DictReader(io.StringIO(raw.decode("utf-8"))):
        try:
            value = float(row["Value"])
            year = int(row["Year"])
        except (ValueError, KeyError):
            continue
        if math.isnan(value):
            continue
        out.setdefault(row["Country Code"], {})[year] = value
    return out


def load_imf_debt(refresh: bool) -> dict[str, dict[int, float]]:
    try:
        raw = fetch(IMF_DEBT_URL, "imf_GGXWDG_NGDP.json", refresh, timeout=30)
        values = json.loads(raw)["values"]["GGXWDG_NGDP"]
    except Exception as e:  # noqa: BLE001 — любой сбой сети или формата означает «нет данных МВФ»
        print(f"  данные МВФ недоступны ({e.__class__.__name__}), госдолг — из Всемирного банка")
        return {}
    return {iso: {int(y): float(v) for y, v in series.items() if v is not None} for iso, series in values.items()}


# ---------- проекция Equal Earth ----------

A1, A2, A3, A4 = 1.340264, -0.081106, 0.000893, 0.003796
M = math.sqrt(3) / 2
EE_X_MAX = 2.7066297319215575  # x на долготе 180° и широте 0°
SCALE = MAP_WIDTH / (2 * EE_X_MAX)


def project(lon: float, lat: float) -> tuple[float, float]:
    lam = math.radians(lon)
    th = math.asin(M * math.sin(math.radians(lat)))
    th2 = th * th
    th6 = th2 * th2 * th2
    x = 2 * math.sqrt(3) * lam * math.cos(th) / (3 * (9 * A4 * th6 * th2 + 7 * A3 * th6 + 3 * A2 * th2 + A1))
    y = th * (A1 + A2 * th2 + th6 * (A3 + A4 * th2))
    # экранная ось y направлена вниз
    return round(x * SCALE, 1), round(-y * SCALE, 1)


# ---------- страны ----------

def load_reference(refresh: bool) -> dict[str, dict]:
    ref = json.loads(fetch(COUNTRIES_REF, "countries_ref.json", refresh))
    return {c["cca3"]: c for c in ref if c.get("unMember") and c["cca3"] != "VAT"}


def income_from(value: float) -> str:
    for limit, group in INCOME_THRESHOLDS:
        if value <= limit:
            return group
    return "high"


def latest(series: dict[int, float] | None, min_year: int) -> tuple[float, int] | None:
    if not series:
        return None
    for year in range(LATEST_YEAR, min_year - 1, -1):
        if year in series:
            return series[year], year
    return None


def build_countries(ref: dict[str, dict], ne_by_iso: dict[str, dict], capitals: dict[str, str], refresh: bool):
    wdi: dict[str, dict[str, dict[int, float]]] = {}
    for _, codes, _, _ in INDICATORS:
        for code in codes:
            wdi[code] = load_wdi(code, refresh)
    wdi[GNI_ATLAS] = load_wdi(GNI_ATLAS, refresh)
    imf_debt = load_imf_debt(refresh)

    countries = []
    for iso, c in sorted(ref.items()):
        rus = c["translations"]["rus"]
        ne = ne_by_iso.get(iso, {})
        name = NAME_FIXES.get(iso, rus["common"])
        entry = {
            "id": iso,
            "iso2": c["cca2"],
            "name": name,
            "short_name": SHORT_NAMES.get(iso, name),
            "official_name": OFFICIAL_FIXES.get(iso, rus["official"]),
            "name_en": c["name"]["common"],
            "capital": CAPITAL_FIXES.get(iso) or capitals.get(iso) or (c["capital"][0] if c.get("capital") else ""),
            "region": REGION_RU.get(c["region"], c["region"]),
            "subregion": SUBREGION_RU.get(c["subregion"], c["subregion"]),
            "area_km2": c.get("area"),
            "landlocked": bool(c.get("landlocked")),
            "income_group": None,
            "income_source": None,
            "stats": {},
        }

        for key, codes, min_year, _ in INDICATORS:
            for code in codes:
                found = latest(wdi[code].get(iso), min_year)
                if found:
                    value, year = found
                    entry["stats"][key] = {"value": round(value, 4), "year": year, "source": f"wb:{code}"}
                    break

        imf = latest(imf_debt.get(iso), 2018)
        if imf:
            entry["stats"]["gov_debt_pct_gdp"] = {"value": round(imf[0], 4), "year": imf[1], "source": "imf:GGXWDG_NGDP"}

        gni = latest({y: v for y, v in wdi[GNI_ATLAS].get(iso, {}).items() if y <= INCOME_YEAR}, 2018)
        gdp_pc = entry["stats"].get("gdp_per_capita_usd")
        if gni:
            entry["income_group"] = income_from(gni[0])
            entry["income_source"] = f"wb:{GNI_ATLAS}:{gni[1]}"
        elif gdp_pc:
            entry["income_group"] = income_from(gdp_pc["value"])
            entry["income_source"] = f"wb:NY.GDP.PCAP.CD:{gdp_pc['year']}"
        else:
            entry["income_group"] = NE_INCOME.get(ne.get("INCOME_GRP", ""), "low")
            entry["income_source"] = "natural_earth:INCOME_GRP"
        countries.append(entry)

    fill_gaps(countries)
    return countries


def median_fill(countries: list[dict], key: str, target: dict) -> dict | None:
    """Медиана по субрегиону и группе дохода, затем по субрегиону, по группе дохода, по миру."""
    def pick(pred):
        vals = [c["stats"][key]["value"] for c in countries
                if key in c["stats"] and c["stats"][key]["source"] not in ("fill", "derived") and pred(c)]
        return vals

    attempts = [
        ("субрегион и группа дохода", lambda c: c["subregion"] == target["subregion"] and c["income_group"] == target["income_group"]),
        ("субрегион", lambda c: c["subregion"] == target["subregion"]),
        ("группа дохода", lambda c: c["income_group"] == target["income_group"]),
        ("весь мир", lambda c: True),
    ]
    for label, pred in attempts:
        vals = pick(pred)
        if len(vals) >= 3:
            return {"value": round(statistics.median(vals), 4), "year": None, "source": "fill",
                    "method": f"медиана: {label}", "n": len(vals)}
    return None


def fill_gaps(countries: list[dict]) -> None:
    by_key = {k: mode for k, _, _, mode in INDICATORS}
    for key, mode in by_key.items():
        if mode != "median":
            continue
        for c in countries:
            if key not in c["stats"]:
                filled = median_fill(countries, key, c)
                if filled:
                    c["stats"][key] = filled

    # Производные показатели: ВВП из ВВП на душу и наоборот, рабочая сила из доли в населении.
    lf_share = statistics.median(
        c["stats"]["labor_force"]["value"] / c["stats"]["population"]["value"]
        for c in countries if "labor_force" in c["stats"] and "population" in c["stats"])
    for c in countries:
        s = c["stats"]
        if "population" not in s:
            raise SystemExit(f"Нет населения для {c['id']} — без него страну не собрать")
        pop = s["population"]["value"]
        if "gdp_usd" not in s:
            pc = s["gdp_per_capita_usd"]
            s["gdp_usd"] = {"value": round(pc["value"] * pop, 0), "year": pc["year"], "source": "derived",
                            "method": "ВВП на душу × население"}
        if "labor_force" not in s:
            s["labor_force"] = {"value": round(pop * lf_share, 0), "year": s["population"]["year"], "source": "derived",
                                "method": f"население × медианная доля рабочей силы ({lf_share:.2f})"}


# ---------- карта ----------

def owner_of(p: dict, un: set[str], admin_to_iso: dict[str, str]) -> str | None:
    a3 = p["ADM0_A3"]
    if a3 in NEUTRAL or a3 in SKIP:
        return None
    for code in (p["ISO_A3_EH"], p["ISO_A3"], a3):
        if code in un:
            return code
    return admin_to_iso.get(p["SOVEREIGNT"])


def polygons_of(geometry: dict) -> list[list[list[list[float]]]]:
    if geometry["type"] == "Polygon":
        return [geometry["coordinates"]]
    if geometry["type"] == "MultiPolygon":
        return geometry["coordinates"]
    return []


def build_polygon(rings_lonlat: list[list[list[float]]]) -> dict | None:
    rings = []
    for ring in rings_lonlat:
        pts = []
        for lon, lat in ring[:-1] if ring[0] == ring[-1] else ring:
            pt = project(lon, lat)
            if not pts or pts[-1] != pt:
                pts.append(pt)
        if len(pts) > 1 and pts[0] == pts[-1]:
            pts.pop()
        if len(pts) >= 3:
            rings.append(pts)
    if not rings:
        return None
    verts = np.array([pt for ring in rings for pt in ring], dtype=np.float64)
    ends = np.cumsum([len(r) for r in rings]).astype(np.uint32)
    tris = earcut.triangulate_float64(verts, ends)
    if len(tris) == 0:
        return None
    return {
        "rings": [[v for pt in ring for v in pt] for ring in rings],
        "triangles": [int(i) for i in tris],
    }


def color_countries(adjacency: dict[str, set[str]]) -> dict[str, int]:
    """Жадная раскраска: соседи получают разные цвета, цвета используются равномерно."""
    colors: dict[str, int] = {}
    usage = [0] * POLITICAL_COLORS
    for iso in sorted(adjacency, key=lambda k: (-len(adjacency[k]), k)):
        taken = {colors[n] for n in adjacency[iso] if n in colors}
        free = [i for i in range(POLITICAL_COLORS) if i not in taken] or list(range(POLITICAL_COLORS))
        choice = min(free, key=lambda i: (usage[i], i))
        colors[iso] = choice
        usage[choice] += 1
    return colors


def build_map(ne: dict, un: set[str]):
    feats = ne["features"]
    admin_to_iso: dict[str, str] = {}
    for f in feats:
        p = f["properties"]
        for code in (p["ISO_A3_EH"], p["ISO_A3"], p["ADM0_A3"]):
            if code in un:
                admin_to_iso[p["ADMIN"]] = code
                break

    countries: dict[str, dict] = {}
    territories: list[dict] = []
    vertex_owner: dict[tuple[float, float], set[str]] = {}
    ne_by_iso: dict[str, dict] = {}
    adm0_owner: dict[str, str] = {}

    for f in feats:
        p = f["properties"]
        a3 = p["ADM0_A3"]
        if a3 in SKIP:
            continue
        owner = owner_of(p, un, admin_to_iso)
        polys = [pg for pg in (build_polygon(r) for r in polygons_of(f["geometry"])) if pg]
        if owner is None:
            if a3 not in NEUTRAL:
                print(f"  внимание: {p['ADMIN']} ({a3}) без владельца, рисую как нейтральную территорию")
            territories.append({
                "id": a3,
                "name": NEUTRAL_NAMES.get(a3) or p.get("NAME_RU") or p["NAME"],
                "note": NEUTRAL.get(a3, "территория без государства — члена ООН"),
                "label": list(project(p["LABEL_X"], p["LABEL_Y"])),
                "polygons": polys,
            })
            continue
        adm0_owner[a3] = owner
        entry = countries.setdefault(owner, {"id": owner, "label": None, "polygons": []})
        entry["polygons"].extend(polys)
        is_main = p["ADMIN"] == p["SOVEREIGNT"] or owner in (p["ISO_A3_EH"], p["ISO_A3"])
        if is_main and entry["label"] is None:
            entry["label"] = list(project(p["LABEL_X"], p["LABEL_Y"]))
            ne_by_iso[owner] = p
        for poly in polygons_of(f["geometry"]):
            for ring in poly:
                for lon, lat in ring:
                    vertex_owner.setdefault((round(lon, 3), round(lat, 3)), set()).add(owner)

    adjacency: dict[str, set[str]] = {iso: set() for iso in countries}
    for owners in vertex_owner.values():
        if len(owners) > 1:
            for a in owners:
                adjacency[a] |= owners - {a}
    colors = color_countries(adjacency)

    missing = un - set(countries)
    if missing:
        raise SystemExit(f"На карте нет стран ООН: {sorted(missing)}")

    out_countries = []
    for iso in sorted(countries):
        c = countries[iso]
        out_countries.append({
            "id": iso,
            "label": c["label"],
            "color": colors[iso],
            "neighbors": sorted(adjacency[iso]),
            "polygons": c["polygons"],
        })

    ys = [v for c in out_countries for pg in c["polygons"] for ring in pg["rings"] for v in ring[1::2]]
    world = {
        "projection": "equal_earth",
        "scale": round(SCALE, 6),
        "width": MAP_WIDTH,
        "min_y": min(ys),
        "max_y": max(ys),
        "political_colors": POLITICAL_COLORS,
        "countries": out_countries,
        "territories": territories,
    }
    return world, ne_by_iso, adm0_owner


def build_cities(places: dict, adm0_owner: dict[str, str], ref: dict[str, dict]):
    cities = []
    capitals: dict[str, str] = {}
    by_country: dict[str, list[dict]] = {}
    for f in places["features"]:
        p = f["properties"]
        owner = adm0_owner.get(p["ADM0_A3"])
        if owner is None:
            continue
        is_capital = p["FEATURECLA"] == "Admin-0 capital"
        name = p.get("NAME_RU") or p["NAME"]
        x, y = project(p["LONGITUDE"], p["LATITUDE"])
        city = {"name": name, "country": owner, "x": x, "y": y, "population": int(p["POP_MAX"] or 0),
                "capital": is_capital, "rank": int(p["SCALERANK"])}
        by_country.setdefault(owner, []).append(city)
        if is_capital:
            ref_caps = ref.get(owner, {}).get("capital") or []
            if owner not in capitals or p["NAME"] in ref_caps or p["NAMEASCII"] in ref_caps:
                capitals[owner] = name

    for owner, lst in by_country.items():
        lst.sort(key=lambda c: -c["population"])
        for i, c in enumerate(lst):
            if c["capital"] or c["population"] >= 1_000_000 or i < 3:
                c["capital"] = c["capital"] and capitals.get(owner) == c["name"]
                cities.append(c)
    cities.sort(key=lambda c: (c["country"], -c["population"]))
    return cities, capitals


def write_json(path: pathlib.Path, obj, compact: bool = False) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open("w", encoding="utf-8", newline="\n") as fh:
        if compact:
            json.dump(obj, fh, ensure_ascii=False, separators=(",", ":"))
        else:
            json.dump(obj, fh, ensure_ascii=False, indent=1)
        fh.write("\n")
    print(f"  записан {path.relative_to(ROOT)} ({path.stat().st_size // 1024} КБ)")


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--refresh", action="store_true", help="скачать источники заново")
    args = parser.parse_args()

    print("1/4 Справочник стран")
    ref = load_reference(args.refresh)
    un = set(ref)
    print(f"  членов ООН: {len(un)}")

    print("2/4 Границы и карта")
    ne = json.loads(fetch(NE_COUNTRIES, "ne_50m_admin_0_countries.geojson", args.refresh))
    world, ne_by_iso, adm0_owner = build_map(ne, un)

    print("3/4 Города")
    places = json.loads(fetch(NE_PLACES, "ne_50m_populated_places.geojson", args.refresh))
    cities, capitals = build_cities(places, adm0_owner, ref)

    print("4/4 Показатели Всемирного банка")
    countries = build_countries(ref, ne_by_iso, capitals, args.refresh)
    filled = sum(1 for c in countries for s in c["stats"].values() if s["source"] in ("fill", "derived"))
    total = sum(len(c["stats"]) for c in countries)
    print(f"  значений: {total}, из них заполнено оценками: {filled}")

    built = dt.date.today().isoformat()
    write_json(DATA / "countries.json", {
        "built": built,
        "sources": {
            "wb": "Всемирный банк, World Development Indicators (CC BY 4.0)",
            "imf": "МВФ, World Economic Outlook",
            "fill": "Пропуск заполнен медианой по похожим странам",
            "derived": "Вычислено из других показателей",
            "natural_earth": "Natural Earth (общественное достояние)",
        },
        "countries": countries,
    })
    write_json(DATA / "map" / "world.json", {"built": built, **world}, compact=True)
    write_json(DATA / "map" / "cities.json", {"built": built, "cities": cities})


if __name__ == "__main__":
    main()
