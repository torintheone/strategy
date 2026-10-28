#!/usr/bin/env python3
"""Сборка данных мира для Politeconomy.

Собирает из открытых источников:
  data/countries.json   — страны и стартовые показатели
  data/regions.json     — регионы стран: название, столица, население
  data/map/world.json   — геометрия карты по регионам, границы, подписи
  data/map/cities.json  — города с привязкой к регионам

Показатели берутся из снимков в tools/sources/ (их скачивает tools/fetch_sources.py
в GitHub Actions), геометрия — из Natural Earth 1:10m (скачивается в tools/.cache).
Нужны Python-пакеты из tools/requirements.txt и mapshaper: «npm install --prefix tools».

Запуск:  python tools/build_world.py [--refresh]
"""

from __future__ import annotations

import argparse
import datetime as dt
import json
import math
import pathlib
import statistics
import sys
import unicodedata
import urllib.request

import mapbox_earcut as earcut
import numpy as np
from shapely.geometry import Point, shape
from shapely.strtree import STRtree

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
import regions as R  # noqa: E402

ROOT = pathlib.Path(__file__).resolve().parent.parent
TOOLS = ROOT / "tools"
CACHE = TOOLS / ".cache"
SOURCES = TOOLS / "sources"
DATA = ROOT / "data"

# Natural Earth — официальный репозиторий проекта на GitHub, версия 5.1.2.
NE_BASE = "https://raw.githubusercontent.com/nvkelso/natural-earth-vector/v5.1.2/geojson/"
NE_ADMIN0 = NE_BASE + "ne_10m_admin_0_countries.geojson"
NE_ADMIN1 = NE_BASE + "ne_10m_admin_1_states_provinces.geojson"
NE_PLACES = NE_BASE + "ne_10m_populated_places.geojson"
# Справочник стран: коды ISO, членство в ООН, русские названия, регионы (ODbL).
COUNTRIES_REF = "https://raw.githubusercontent.com/mledoze/countries/master/countries.json"

LATEST_YEAR = 2025
SIMPLIFY = "10%"

# Показатель -> источники по приоритету, самый ранний допустимый год, способ заполнения пропусков.
# Источник: ("wb", код), ("imf", код, множитель) или ("ne", поле Natural Earth, множитель).
INDICATORS = [
    ("population", [("wb", "SP.POP.TOTL"), ("imf", "LP", 1e6), ("ne", "POP_EST", 1)], 2015, "required"),
    ("gdp_usd", [("wb", "NY.GDP.MKTP.CD"), ("imf", "NGDPD", 1e9), ("ne", "GDP_MD", 1e6)], 2015, "derived"),
    ("gdp_per_capita_usd", [("wb", "NY.GDP.PCAP.CD"), ("imf", "NGDPDPC", 1)], 2015, "median"),
    ("gdp_ppp_per_capita", [("wb", "NY.GDP.PCAP.PP.CD"), ("imf", "PPPPC", 1)], 2015, "median"),
    ("gdp_growth_pct", [("wb", "NY.GDP.MKTP.KD.ZG"), ("imf", "NGDP_RPCH", 1)], 2020, "median"),
    ("inflation_pct", [("wb", "FP.CPI.TOTL.ZG"), ("imf", "PCPIPCH", 1), ("wb", "NY.GDP.DEFL.KD.ZG")], 2020, "median"),
    ("unemployment_pct", [("wb", "SL.UEM.TOTL.ZS"), ("imf", "LUR", 1)], 2020, "median"),
    ("gov_debt_pct_gdp", [("imf", "GGXWDG_NGDP", 1), ("wb", "GC.DOD.TOTL.GD.ZS")], 2015, "median"),
    ("budget_balance_pct_gdp", [("imf", "GGXCNL_NGDP", 1)], 2015, "median"),
    ("gov_revenue_pct_gdp", [("imf", "rev", 1)], 2015, "median"),
    ("gov_spending_pct_gdp", [("imf", "exp", 1)], 2015, "median"),
    ("tax_revenue_pct_gdp", [("wb", "GC.TAX.TOTL.GD.ZS")], 2015, "median"),
    ("current_account_pct_gdp", [("imf", "BCA_NGDPD", 1)], 2015, "median"),
    ("exports_pct_gdp", [("wb", "NE.EXP.GNFS.ZS")], 2015, "median"),
    ("imports_pct_gdp", [("wb", "NE.IMP.GNFS.ZS")], 2015, "median"),
    ("agriculture_pct_gdp", [("wb", "NV.AGR.TOTL.ZS")], 2015, "median"),
    ("industry_pct_gdp", [("wb", "NV.IND.TOTL.ZS")], 2015, "median"),
    ("services_pct_gdp", [("wb", "NV.SRV.TOTL.ZS")], 2015, "median"),
    ("life_expectancy", [("wb", "SP.DYN.LE00.IN")], 2015, "median"),
    ("fertility_rate", [("wb", "SP.DYN.TFRT.IN")], 2015, "median"),
    ("urban_pct", [("wb", "SP.URB.TOTL.IN.ZS")], 2015, "median"),
    ("age_0_14_pct", [("wb", "SP.POP.0014.TO.ZS")], 2015, "median"),
    ("age_65_plus_pct", [("wb", "SP.POP.65UP.TO.ZS")], 2015, "median"),
    ("labor_force", [("wb", "SL.TLF.TOTL.IN")], 2015, "derived"),
    ("military_pct_gdp", [("wb", "MS.MIL.XPND.GD.ZS")], 2015, "median"),
    ("electricity_access_pct", [("wb", "EG.ELC.ACCS.ZS")], 2015, "median"),
    ("gini", [("wb", "SI.POV.GINI")], 2005, "median"),
    # структура экономики — для калибровки отраслей (этап 1)
    ("manufacturing_pct_gdp", [("wb", "NV.IND.MANF.ZS")], 2015, "median"),
    ("resource_rents_pct_gdp", [("wb", "NY.GDP.TOTL.RT.ZS")], 2015, "median"),
    ("oil_rents_pct_gdp", [("wb", "NY.GDP.PETR.RT.ZS")], 2015, "median"),
    ("gas_rents_pct_gdp", [("wb", "NY.GDP.NGAS.RT.ZS")], 2015, "median"),
    ("coal_rents_pct_gdp", [("wb", "NY.GDP.COAL.RT.ZS")], 2015, "median"),
    ("mineral_rents_pct_gdp", [("wb", "NY.GDP.MINR.RT.ZS")], 2015, "median"),
    ("investment_pct_gdp", [("wb", "NE.GDI.TOTL.ZS")], 2015, "median"),
    ("consumption_private_pct_gdp", [("wb", "NE.CON.PRVT.ZS")], 2015, "median"),
    ("consumption_gov_pct_gdp", [("wb", "NE.CON.GOVT.ZS")], 2015, "median"),
    ("employment_agriculture_pct", [("wb", "SL.AGR.EMPL.ZS")], 2015, "median"),
    ("employment_industry_pct", [("wb", "SL.IND.EMPL.ZS")], 2015, "median"),
    ("employment_services_pct", [("wb", "SL.SRV.EMPL.ZS")], 2015, "median"),
    ("population_growth_pct", [("wb", "SP.POP.GROW")], 2015, "median"),
]
GNI_ATLAS = "NY.GNP.PCAP.CD"
# Правдоподобные пределы: значение вне них считаем ошибкой источника и заполняем как пропуск.
PLAUSIBLE = {"investment_pct_gdp": (3, 70), "consumption_private_pct_gdp": (20, 120), "consumption_gov_pct_gdp": (2, 60)}
# Ренту усредняем за несколько лет: в отдельные годы она скачет вместе с ценами на сырьё.
AVERAGED = {"resource_rents_pct_gdp", "oil_rents_pct_gdp", "gas_rents_pct_gdp", "coal_rents_pct_gdp", "mineral_rents_pct_gdp"}
AVERAGE_FROM = 2019

# Прогноз МВФ: тренд роста и инфляции, к которым модель сходится в первые годы.
# Тренд — 2029–2030: в прогнозах на 2026–2028 годы у части стран (Персидский залив) заложены события 2026 года
# и восстановление после них, которых в сценарии со стартом 1 января 2026 года ещё не было.
TREND_YEARS = (2029, 2030)
FORECAST_YEAR = 2026
ANCHOR_YEAR = 2030

# Валюта страны, если в справочнике их несколько или нет ни одной.
CURRENCY_OVERRIDES = {
    "BHS": "BSD", "BRN": "BND", "BTN": "BTN", "FSM": "USD", "KHM": "KHR", "KIR": "AUD", "LSO": "LSL", "NAM": "NAD",
    "PAN": "USD", "SWZ": "SZL", "TUV": "AUD", "ZWE": "ZWG", "XKX": "EUR", "PSE": "ILS", "SOL": "SLS", "CYN": "TRY",
    "TWN": "TWD",
}
# Коды BIS для стран зоны евро нет: ставку ЕЦБ BIS даёт под кодом XM.
BIS_EURO = "XM"

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

REGION_RU = {"Africa": "Африка", "Americas": "Америка", "Asia": "Азия", "Europe": "Европа", "Oceania": "Океания"}
SUBREGION_RU = {
    "Australia and New Zealand": "Австралия и Новая Зеландия", "Caribbean": "Карибский бассейн",
    "Central America": "Центральная Америка", "Central Asia": "Центральная Азия", "Central Europe": "Центральная Европа",
    "Eastern Africa": "Восточная Африка", "Eastern Asia": "Восточная Азия", "Eastern Europe": "Восточная Европа",
    "Melanesia": "Меланезия", "Micronesia": "Микронезия", "Middle Africa": "Центральная Африка",
    "North America": "Северная Америка", "Northern Africa": "Северная Африка", "Northern Europe": "Северная Европа",
    "Polynesia": "Полинезия", "South America": "Южная Америка", "South-Eastern Asia": "Юго-Восточная Азия",
    "Southeast Europe": "Юго-Восточная Европа", "Southern Africa": "Южная Африка", "Southern Asia": "Южная Азия",
    "Southern Europe": "Южная Европа", "Western Africa": "Западная Африка", "Western Asia": "Западная Азия",
    "Western Europe": "Западная Европа",
}

# Короткие названия для карты и интерфейса там, где обычное слишком длинное.
SHORT_NAMES = {
    "USA": "США", "ARE": "ОАЭ", "CAF": "ЦАР", "COD": "ДР Конго", "COG": "Конго", "DOM": "Доминикана",
    "BIH": "Босния и Герцеговина", "GNQ": "Экв. Гвинея", "PNG": "Папуа — Новая Гвинея", "VCT": "Сент-Винсент",
    "KNA": "Сент-Китс и Невис", "STP": "Сан-Томе и Принсипи", "FSM": "Микронезия", "MHL": "Маршалловы Острова",
}
NAME_FIXES = {"USA": "США", "PRK": "КНДР"}
OFFICIAL_FIXES = {
    "PRK": "Корейская Народно-Демократическая Республика",
    "TLS": "Демократическая Республика Тимор-Лешти",
}
CAPITAL_FIXES = {"NRU": "Ярен", "SSD": "Джуба"}
# Русские названия зависимых территорий, которые Natural Earth даёт с ударениями или по-английски.
TYPE_RU = {
    "State": "штат", "Province": "провинция", "Region": "регион", "City": "город", "Independent City": "город",
    "Federal District": "федеральный округ", "District": "район", "County": "графство", "Municipality": "муниципалитет",
    "Oblast": "область", "Autonomous Republic": "автономная республика", "Department": "департамент",
    "Krai": "край", "Republic": "республика", "Territory": "территория", "Capital District": "столичный округ",
}

MAP_WIDTH = 8192.0
POLITICAL_COLORS = 8


# ---------- загрузка ----------

def fetch(url: str, name: str, refresh: bool) -> bytes:
    CACHE.mkdir(parents=True, exist_ok=True)
    path = CACHE / name
    if path.exists() and not refresh:
        return path.read_bytes()
    print(f"  скачиваю {url}")
    with urllib.request.urlopen(url, timeout=300) as r:
        data = r.read()
    path.write_bytes(data)
    return data


def load_snapshot(name: str) -> dict:
    path = SOURCES / name
    if not path.exists():
        raise SystemExit(f"Нет снимка {path.relative_to(ROOT)}: запустите workflow «Источники данных» или tools/fetch_sources.py")
    return json.loads(path.read_text(encoding="utf-8"))


def series(values: dict, code: str, country: str) -> dict[int, float]:
    return {int(y): v for y, v in values.get(code, {}).get(country, {}).items() if v is not None}


def clean_name(text: str) -> str:
    """Убирает знаки ударения, которые встречаются в русских названиях Natural Earth."""
    return unicodedata.normalize("NFC", "".join(ch for ch in unicodedata.normalize("NFD", text) if ch != "́"))


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


def project_ring(ring) -> list[tuple[float, float]]:
    pts = []
    coords = list(ring)
    if len(coords) > 1 and coords[0] == coords[-1]:
        coords = coords[:-1]
    for lon, lat in coords:
        pt = project(lon, lat)
        if not pts or pts[-1] != pt:
            pts.append(pt)
    if len(pts) > 1 and pts[0] == pts[-1]:
        pts.pop()
    return pts


def polygons_of(geom: dict) -> list:
    if geom["type"] == "Polygon":
        return [geom["coordinates"]]
    if geom["type"] == "MultiPolygon":
        return geom["coordinates"]
    return []


def build_polygon(rings_lonlat) -> dict | None:
    rings = [r for r in (project_ring(ring) for ring in rings_lonlat) if len(r) >= 3]
    if not rings:
        return None
    verts = np.array([pt for ring in rings for pt in ring], dtype=np.float64)
    ends = np.cumsum([len(r) for r in rings]).astype(np.uint32)
    tris = earcut.triangulate_float64(verts, ends)
    if len(tris) == 0:
        return None
    return {"rings": [[v for pt in ring for v in pt] for ring in rings], "triangles": [int(i) for i in tris]}


def flat(points) -> list[float]:
    return [v for pt in points for v in pt]


# ---------- страны ----------

def income_from(value: float) -> str:
    for limit, group in INCOME_THRESHOLDS:
        if value <= limit:
            return group
    return "high"


def latest(values: dict[int, float] | None, min_year: int, max_year: int = LATEST_YEAR) -> tuple[float, int] | None:
    if not values:
        return None
    for year in range(max_year, min_year - 1, -1):
        if year in values:
            return values[year], year
    return None


def average(values: dict[int, float] | None, min_year: int, max_year: int = LATEST_YEAR) -> tuple[float, int] | None:
    years = [y for y in (values or {}) if min_year <= y <= max_year]
    if not years:
        return None
    return sum(values[y] for y in years) / len(years), max(years)


def country_records(ref: dict[str, dict], ref_all: dict[str, dict], extras: list[dict], ne0_by_owner: dict[str, dict],
                    capitals: dict[str, str]) -> list[dict]:
    records = []
    for iso, c in sorted(ref.items()):
        rus = c["translations"]["rus"]
        name = NAME_FIXES.get(iso, rus["common"])
        records.append({
            "id": iso, "iso2": c["cca2"], "name": name, "short_name": SHORT_NAMES.get(iso, name),
            "official_name": OFFICIAL_FIXES.get(iso, rus["official"]), "name_en": c["name"]["common"],
            "capital": CAPITAL_FIXES.get(iso) or capitals.get(iso) or (c["capital"][0] if c.get("capital") else ""),
            "region": REGION_RU.get(c["region"], c["region"]),
            "subregion": SUBREGION_RU.get(c["subregion"], c["subregion"]),
            "area_km2": c.get("area"), "landlocked": bool(c.get("landlocked")),
            "un_member": True, "status_note": None,
            "currency": CURRENCY_OVERRIDES.get(iso) or next(iter(c.get("currencies") or {}), ""),
            "_wb": iso, "_imf": iso, "_ne": ne0_by_owner.get(iso, {}), "_ne_stats": [],
        })
    for e in extras:
        ne = ne0_by_owner.get(e["id"], {})
        records.append({
            "id": e["id"], "iso2": e["iso2"], "name": e["name"], "short_name": e["name"],
            "official_name": e["official_name"], "name_en": ne.get("NAME_EN") or ne.get("ADMIN") or e["name"],
            "capital": e["capital"], "region": e["region"], "subregion": e["subregion"],
            "area_km2": e.get("area_km2") or ref_all.get(e["ref"] or "", {}).get("area"), "landlocked": False,
            "un_member": False, "status_note": e["note"],
            "currency": CURRENCY_OVERRIDES.get(e["id"], ""),
            "_wb": e["wb"], "_imf": e["imf"], "_ne": ne, "_ne_stats": e.get("ne_stats", []),
        })
    return records


def build_countries(records: list[dict], wb: dict, imf: dict, bis: dict) -> list[dict]:
    countries = []
    for rec in records:
        entry = {k: v for k, v in rec.items() if not k.startswith("_")}
        entry["income_group"] = None
        entry["income_source"] = None
        entry["stats"] = {}
        for key, sources, min_year, _ in INDICATORS:
            # Берём самое свежее значение из Всемирного банка и МВФ; при равном годе — по порядку источников.
            candidates = []
            for src in sources:
                found, label = None, None
                if src[0] == "wb" and rec["_wb"]:
                    pick = average if key in AVERAGED else latest
                    found = pick(series(wb, src[1], rec["_wb"]), AVERAGE_FROM if key in AVERAGED else min_year)
                    label = f"wb:{src[1]}"
                elif src[0] == "imf" and rec["_imf"]:
                    hit = latest(series(imf, src[1], rec["_imf"]), min_year)
                    found = (hit[0] * src[2], hit[1]) if hit else None
                    label = f"imf:{src[1]}"
                elif src[0] == "ne" and key in rec["_ne_stats"] and rec["_ne"].get(src[1]):
                    year = rec["_ne"].get("POP_YEAR" if key == "population" else "GDP_YEAR")
                    found = (float(rec["_ne"][src[1]]) * src[2], int(year) if year else None)
                    label = "natural_earth"
                if found and key in PLAUSIBLE and not PLAUSIBLE[key][0] <= found[0] <= PLAUSIBLE[key][1]:
                    found = None
                if found:
                    candidates.append((found, label))
            measured = [c for c in candidates if c[1] != "natural_earth"]
            if measured:
                found, label = max(measured, key=lambda c: c[0][1] or 0)  # max берёт первый из равных
            elif candidates:
                found, label = candidates[0]
            else:
                continue
            entry["stats"][key] = {"value": round(found[0], 4), "year": found[1], "source": label}
            if key in AVERAGED:
                entry["stats"][key]["method"] = f"среднее за {AVERAGE_FROM}–{found[1]}"
        add_economy_stats(entry, rec, wb, imf, bis)

        gni = latest(series(wb, GNI_ATLAS, rec["_wb"]) if rec["_wb"] else None, 2018, INCOME_YEAR)
        gdp_pc = entry["stats"].get("gdp_per_capita_usd")
        if gni:
            entry["income_group"] = income_from(gni[0])
            entry["income_source"] = f"wb:{GNI_ATLAS}:{gni[1]}"
        elif gdp_pc:
            entry["income_group"] = income_from(gdp_pc["value"])
            entry["income_source"] = f"{gdp_pc['source']}:{gdp_pc['year']}"
        else:
            entry["income_group"] = NE_INCOME.get(rec["_ne"].get("INCOME_GRP", ""), "low")
            entry["income_source"] = "natural_earth:INCOME_GRP"
        countries.append(entry)

    fill_gaps(countries)
    fill_economy(countries)
    return countries


def add_economy_stats(entry: dict, rec: dict, wb: dict, imf: dict, bis: dict) -> None:
    """Показатели для калибровки модели экономики: прогнозы МВФ, проценты по долгу, ставка ЦБ, туризм, резервы."""
    stats = entry["stats"]
    if rec["_imf"]:
        growth = series(imf, "NGDP_RPCH", rec["_imf"])
        trend = [growth[y] for y in range(TREND_YEARS[0], TREND_YEARS[1] + 1) if y in growth]
        if len(trend) >= 2:
            stats["gdp_growth_trend_pct"] = {"value": round(sum(trend) / len(trend), 4), "year": TREND_YEARS[1],
                                             "source": "imf:NGDP_RPCH",
                                             "method": f"среднее прогноза на {TREND_YEARS[0]}–{TREND_YEARS[1]}"}
        if FORECAST_YEAR in growth:
            stats["gdp_growth_forecast_pct"] = {"value": growth[FORECAST_YEAR], "year": FORECAST_YEAR,
                                                "source": "imf:NGDP_RPCH", "method": "прогноз"}
        infl = series(imf, "PCPIPCH", rec["_imf"])
        if ANCHOR_YEAR in infl:
            stats["inflation_anchor_pct"] = {"value": infl[ANCHOR_YEAR], "year": ANCHOR_YEAR,
                                             "source": "imf:PCPIPCH", "method": "прогноз"}
        lur = series(imf, "LUR", rec["_imf"])
        if ANCHOR_YEAR in lur:
            stats["unemployment_natural_pct"] = {"value": lur[ANCHOR_YEAR], "year": ANCHOR_YEAR,
                                                 "source": "imf:LUR", "method": "прогноз"}
        exp = series(imf, "exp", rec["_imf"])
        prim = series(imf, "prim_exp", rec["_imf"])
        common = [y for y in exp if y in prim and 2018 <= y <= LATEST_YEAR]
        if common:
            y = max(common)
            stats["interest_pct_gdp"] = {"value": round(max(0.0, exp[y] - prim[y]), 4), "year": y,
                                         "source": "imf:exp", "method": "расходы минус первичные расходы"}
    if "interest_pct_gdp" not in stats and rec["_wb"] and "gov_revenue_pct_gdp" in stats:
        hit = latest(series(wb, "GC.XPN.INTP.RV.ZS", rec["_wb"]), 2018)
        if hit:
            stats["interest_pct_gdp"] = {"value": round(hit[0] * stats["gov_revenue_pct_gdp"]["value"] / 100, 4),
                                         "year": hit[1], "source": "wb:GC.XPN.INTP.RV.ZS",
                                         "method": "доля процентов в доходах × доходы бюджета"}
    gdp = stats.get("gdp_usd")
    if rec["_wb"] and gdp:
        for key, code in (("tourism_pct_gdp", "ST.INT.RCPT.CD"), ("reserves_pct_gdp", "FI.RES.TOTL.CD")):
            hit = latest(series(wb, code, rec["_wb"]), 2018)
            if hit:
                stats[key] = {"value": round(100 * hit[0] / gdp["value"], 4), "year": hit[1], "source": f"wb:{code}",
                              "method": "к ВВП"}
    area = BIS_EURO if entry["currency"] == "EUR" and entry["id"] not in ("XKX", "MNE", "AND", "MCO", "SMR") else entry["iso2"]
    rates = bis.get(area) or {}
    months = sorted(m for m in rates if m <= f"{LATEST_YEAR}-12")
    if months:
        m = months[-1]
        stats["policy_rate_pct"] = {"value": rates[m], "year": int(m[:4]), "source": "bis:WS_CBPOL",
                                    "method": f"на {m[5:]}.{m[:4]}"}


def fill_economy(countries: list[dict]) -> None:
    """Пропуски в показателях для калибровки: тождества и медианы, без которых модель не запустить."""
    for c in countries:
        s = c["stats"]
        if "investment_pct_gdp" not in s and all(k in s for k in ("consumption_private_pct_gdp", "consumption_gov_pct_gdp",
                                                                  "exports_pct_gdp", "imports_pct_gdp")):
            inv = 100 - s["consumption_private_pct_gdp"]["value"] - s["consumption_gov_pct_gdp"]["value"] \
                - s["exports_pct_gdp"]["value"] + s["imports_pct_gdp"]["value"]
            if 5 <= inv <= 60 and is_measured(s["consumption_private_pct_gdp"]):
                s["investment_pct_gdp"] = {"value": round(inv, 4), "year": s["consumption_private_pct_gdp"]["year"],
                                           "source": "derived", "method": "100 − потребление − экспорт + импорт"}
    # Бюджет должен сходиться: доходы − расходы = сальдо. Если одна из величин — оценка или за другой год,
    # выводим её из двух других (сальдо МВФ считаем самым надёжным).
    for c in countries:
        s = c["stats"]
        if not all(k in s for k in ("gov_revenue_pct_gdp", "gov_spending_pct_gdp", "budget_balance_pct_gdp")):
            continue
        rev, exp, bal = s["gov_revenue_pct_gdp"], s["gov_spending_pct_gdp"], s["budget_balance_pct_gdp"]
        if abs(rev["value"] - exp["value"] - bal["value"]) <= 1.0:
            continue
        if is_measured(rev) or not is_measured(exp):
            s["gov_spending_pct_gdp"] = {"value": round(rev["value"] - bal["value"], 4), "year": bal["year"], "source": "derived",
                                         "method": "доходы бюджета минус сальдо"}
        else:
            s["gov_revenue_pct_gdp"] = {"value": round(exp["value"] + bal["value"], 4), "year": bal["year"], "source": "derived",
                                        "method": "расходы бюджета плюс сальдо"}

    # Инфляция страны без данных, которая пользуется чужой валютой, — как у эмитента валюты (Северный Кипр — как Турция).
    by_currency: dict[str, list[dict]] = {}
    for c in countries:
        by_currency.setdefault(c["currency"], []).append(c)
    for c in countries:
        for key in ("inflation_pct", "inflation_anchor_pct"):
            if key in c["stats"] and is_measured(c["stats"][key]):
                continue
            issuers = [o for o in by_currency[c["currency"]] if o is not c and key in o["stats"] and is_measured(o["stats"][key])]
            if issuers:
                home = max(issuers, key=lambda o: o["stats"]["gdp_usd"]["value"])
                c["stats"][key] = {"value": home["stats"][key]["value"], "year": home["stats"][key]["year"], "source": "derived",
                                   "method": f"как в стране той же валюты: {home['name']}"}
    rate_by_group: dict[str, float] = {}
    for group in ("low", "lower_middle", "upper_middle", "high"):
        rates = [c["stats"]["interest_pct_gdp"]["value"] / c["stats"]["gov_debt_pct_gdp"]["value"]
                 for c in countries if c["income_group"] == group and "interest_pct_gdp" in c["stats"]
                 and is_measured(c["stats"]["interest_pct_gdp"]) and c["stats"]["gov_debt_pct_gdp"]["value"] > 5]
        rate_by_group[group] = statistics.median(rates) if rates else 0.03
    for c in countries:
        s = c["stats"]
        if "interest_pct_gdp" not in s:
            r = rate_by_group[c["income_group"]]
            s["interest_pct_gdp"] = {"value": round(r * s["gov_debt_pct_gdp"]["value"], 4), "year": None, "source": "derived",
                                     "method": f"госдолг × средняя ставка по группе дохода ({100 * r:.1f}%)"}
        if "unemployment_natural_pct" not in s:
            u = s["unemployment_pct"]
            s["unemployment_natural_pct"] = {"value": u["value"], "year": u["year"], "source": "derived",
                                             "method": "равна текущей безработице"}
        if "gdp_growth_forecast_pct" not in s and "gdp_growth_trend_pct" in s:
            s["gdp_growth_forecast_pct"] = dict(s["gdp_growth_trend_pct"])
    for key in ("investment_pct_gdp", "tourism_pct_gdp", "reserves_pct_gdp", "gdp_growth_trend_pct", "inflation_anchor_pct"):
        for c in countries:
            if key not in c["stats"]:
                filled = median_fill(countries, key, c)
                if filled:
                    c["stats"][key] = filled
    for c in countries:
        s = c["stats"]
        if "gdp_growth_forecast_pct" not in s:
            s["gdp_growth_forecast_pct"] = dict(s["gdp_growth_trend_pct"])


def is_measured(stat: dict) -> bool:
    return stat["source"] not in ("fill", "derived")


def median_fill(countries: list[dict], key: str, target: dict) -> dict | None:
    """Медиана по субрегиону и группе дохода, затем по субрегиону, по группе дохода, по миру."""
    attempts = [
        ("субрегион и группа дохода", lambda c: c["subregion"] == target["subregion"] and c["income_group"] == target["income_group"]),
        ("субрегион", lambda c: c["subregion"] == target["subregion"]),
        ("группа дохода", lambda c: c["income_group"] == target["income_group"]),
        ("весь мир", lambda c: True),
    ]
    for label, pred in attempts:
        vals = [c["stats"][key]["value"] for c in countries if key in c["stats"] and is_measured(c["stats"][key]) and pred(c)]
        if len(vals) >= 3:
            return {"value": round(statistics.median(vals), 4), "year": None, "source": "fill",
                    "method": f"медиана: {label}", "n": len(vals)}
    return None


def fill_gaps(countries: list[dict]) -> None:
    for key, _, _, mode in INDICATORS:
        if mode != "median":
            continue
        for c in countries:
            if key not in c["stats"]:
                filled = median_fill(countries, key, c)
                if filled:
                    c["stats"][key] = filled

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
        elif s["gdp_per_capita_usd"]["source"] == "fill":
            g = s["gdp_usd"]
            s["gdp_per_capita_usd"] = {"value": round(g["value"] / pop, 2), "year": g["year"], "source": "derived",
                                       "method": "ВВП ÷ население"}
        if "labor_force" not in s:
            s["labor_force"] = {"value": round(pop * lf_share, 0), "year": s["population"]["year"], "source": "derived",
                                "method": f"население × медианная доля рабочей силы ({lf_share:.2f})"}


def check_currencies(countries: list[dict]) -> None:
    known = {c["id"]: c for c in json.loads((DATA / "currencies.json").read_text(encoding="utf-8"))["currencies"]}
    missing = [c["id"] for c in countries if not c["currency"]]
    if missing:
        raise SystemExit(f"Нет валюты у стран: {missing} — добавьте в CURRENCY_OVERRIDES")
    used = {c["currency"] for c in countries}
    for cur in known.values():
        anchor = cur.get("anchor")
        if anchor and anchor not in used and anchor not in known:
            raise SystemExit(f"Валюта {cur['id']} привязана к {anchor}, которой нет ни у одной страны")
    print(f"  валют: {len(used)}, из них описаны в currencies.json: {len(used & set(known))}, остальные плавающие")


# ---------- демография ----------

AGE_GROUPS = ["0004", "0509", "1014", "1519", "2024", "2529", "3034", "3539", "4044", "4549", "5054", "5559",
              "6064", "6569", "7074", "7579", "80UP"]
# Похожие страны для государств без данных Всемирного банка.
DEMOGRAPHY_DONORS = {"TWN": "KOR", "SOL": "SOM", "CYN": "CYP"}


def build_population(records: list[dict], countries: list[dict], wb: dict, projections: dict) -> tuple[dict, dict]:
    """Половозрастная структура, рождаемость, смертность, миграция и прогноз для data/population.json."""
    by_id = {c["id"]: c for c in countries}
    wb_of = {r["id"]: r["_wb"] for r in records}

    def last(code: str, iso: str | None, min_year: int = 2015) -> float | None:
        hit = latest(series(wb, code, iso), min_year) if iso else None
        return hit[0] if hit else None

    def structure(iso: str | None) -> tuple[list[float], list[float]] | None:
        if not iso:
            return None
        out = []
        for sex in ("FE", "MA"):
            total = last(f"SP.POP.TOTL.{sex}.IN", iso)
            shares = [last(f"SP.POP.{g}.{sex}.5Y", iso) for g in AGE_GROUPS]
            if total is None or any(x is None for x in shares):
                return None
            norm = sum(shares)
            out.append([total * x / norm for x in shares])
        return out[0], out[1]

    result = []
    for c in countries:
        cid = c["id"]
        iso = wb_of.get(cid)
        donor = wb_of.get(DEMOGRAPHY_DONORS.get(cid, ""), DEMOGRAPHY_DONORS.get(cid))
        ages = structure(iso) or structure(donor)
        if ages is None:
            raise SystemExit(f"Нет возрастной структуры для {cid}")
        female, male = ages
        scale = c["stats"]["population"]["value"] / (sum(female) + sum(male))
        female = [round(x * scale) for x in female]
        male = [round(x * scale) for x in male]

        def value(code: str, fallback: float) -> float:
            v = last(code, iso)
            if v is None and donor:
                v = last(code, donor)
            return fallback if v is None else v

        e0 = c["stats"]["life_expectancy"]["value"]
        e_f = value("SP.DYN.LE00.FE.IN", e0 + 2.5)
        e_m = value("SP.DYN.LE00.MA.IN", e0 - 2.5)
        shift = e0 - (e_f + e_m) / 2  # подгоняем к общей продолжительности жизни страны
        urban = series(wb, "SP.URB.TOTL.IN.ZS", iso) if iso else {}
        years = sorted(y for y in urban if 2015 <= y <= LATEST_YEAR)
        change = (urban[years[-1]] - urban[years[-6]]) / 5 if len(years) >= 6 else 0.3
        entry = {
            "id": cid, "year": LATEST_YEAR, "female": female, "male": male,
            "fertility": c["stats"]["fertility_rate"]["value"],
            "adolescent_fertility": round(value("SP.ADO.TFRT", 40), 3),
            "life_expectancy_female": round(e_f + shift, 3), "life_expectancy_male": round(e_m + shift, 3),
            "infant_mortality": round(value("SP.DYN.IMRT.IN", 20), 3),
            "net_migration": round(average(series(wb, "SM.POP.NETM", iso), 2015)[0]) if iso and series(wb, "SM.POP.NETM", iso) else 0,
            "urban_pct": c["stats"]["urban_pct"]["value"], "urban_change": round(change, 4),
        }
        proj = projections.get("SP.POP.TOTL", {}).get(iso or "")
        if proj:
            years_p = [y for y in range(2025, 2051, 5)
                       if str(y) in projections["SP.DYN.TFRT.IN"].get(iso, {}) and str(y) in projections["SP.DYN.LE00.IN"].get(iso, {})]
            if len(years_p) >= 2:
                entry["path"] = {"years": years_p,
                                 "fertility": [projections["SP.DYN.TFRT.IN"][iso][str(y)] for y in years_p],
                                 "life_expectancy": [projections["SP.DYN.LE00.IN"][iso][str(y)] for y in years_p]}
        result.append(entry)

    benchmarks = {}
    for c in countries:
        iso = wb_of.get(c["id"])
        values = projections.get("SP.POP.TOTL", {}).get(iso or "")
        if values:
            benchmarks[c["id"]] = {y: round(v) for y, v in sorted(values.items())}
    return ({"groups": ["0–4", "5–9", "10–14", "15–19", "20–24", "25–29", "30–34", "35–39", "40–44", "45–49", "50–54",
                        "55–59", "60–64", "65–69", "70–74", "75–79", "80+"], "countries": result},
            {"source": "ООН, World Population Prospects 2024, средний вариант (через базу Всемирного банка)",
             "population": benchmarks})


# ---------- регионы и карта ----------

def resolve_owners(admin0: dict, un: set[str], extras: list[dict]):
    """Код Natural Earth -> страна в игре. Зависимые территории принадлежат метрополиям."""
    extra_by_ne = {e["ne"]: e["id"] for e in extras}
    admin_to: dict[str, str] = {}
    owner_main: dict[str, str] = {}
    for f in admin0["features"]:
        p = f["properties"]
        if p["ADM0_A3"] in extra_by_ne:
            admin_to[p["ADMIN"]] = extra_by_ne[p["ADM0_A3"]]
            owner_main[extra_by_ne[p["ADM0_A3"]]] = p["ADM0_A3"]
            continue
        for code in (p["ISO_A3_EH"], p["ISO_A3"], p["ADM0_A3"]):
            if code in un:
                admin_to[p["ADMIN"]] = code
                owner_main.setdefault(code, p["ADM0_A3"])
                break
    owner_of: dict[str, str | None] = {}
    ne_props: dict[str, dict] = {}
    for f in admin0["features"]:
        p = f["properties"]
        a3 = p["ADM0_A3"]
        ne_props[a3] = p
        if a3 in extra_by_ne:
            owner_of[a3] = extra_by_ne[a3]
            continue
        owner = next((c for c in (p["ISO_A3_EH"], p["ISO_A3"], a3) if c in un), None)
        owner_of[a3] = owner or admin_to.get(p["SOVEREIGNT"])
    return owner_of, owner_main, ne_props


def name_regions(regions: list[R.Region], owner_main: dict[str, str], ne_props: dict[str, dict], geonames: dict) -> None:
    units_per_key: dict[str, int] = {}
    for r in regions:
        for u in r.units:
            units_per_key[u.key] = units_per_key.get(u.key, 0) + 1
    for r in regions:
        key = r.units[0].key
        # Зависимая территория одним регионом называется по самой территории, а не по её части.
        # Регион, занявший всю страну или всю зависимую территорию, называется по ней.
        if r.owner and len(r.units) == units_per_key[key]:
            is_main = key == r.owner or owner_main.get(r.owner) == key
            if not is_main or len(r.units) > 1:
                p = ne_props.get(key if not is_main else owner_main.get(r.owner, key), {})
                r.name = p.get("NAME_RU") or p.get("NAME") or r.name
        r.name = clean_name(r.name)

    # Одинаковые названия в одной стране: сначала русское название GeoNames, потом тип единицы.
    by_owner: dict[str, list[R.Region]] = {}
    for r in regions:
        by_owner.setdefault(r.owner or r.territory or "", []).append(r)
    for group in by_owner.values():
        seen: dict[str, list[R.Region]] = {}
        for r in group:
            seen.setdefault(r.name, []).append(r)
        for name, dups in seen.items():
            if len(dups) < 2:
                continue
            for r in dups:
                if len(r.units) == 1:
                    ru = geonames.get(str(r.units[0].gn_id), {}).get("ru")
                    if ru and ru != name:
                        r.name = clean_name(ru)
            still: dict[str, list[R.Region]] = {}
            for r in dups:
                still.setdefault(r.name, []).append(r)
            for n, rs in still.items():
                if len(rs) < 2:
                    continue
                for i, r in enumerate(sorted(rs, key=lambda x: x.id)):
                    kind = TYPE_RU.get(r.units[0].type_en or "", "")
                    r.name = f"{n} ({kind})" if kind and i > 0 else (n if i == 0 else f"{n} ({i + 1})")


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


def region_populations(regions: list[R.Region], geonames: dict, country_pop: dict[str, float],
                       owner_main: dict[str, str], ne_props: dict[str, dict]) -> dict[str, tuple[float, str]]:
    """Население регионов по GeoNames, пропуски — по плотности, сумма приводится к населению страны.
    Зависимые территории (Гренландия, Пуэрто-Рико) приводятся к собственному населению по Natural Earth."""
    gn_use: dict[int, list[R.Unit]] = {}
    for r in regions:
        for u in r.units:
            if u.gn_id:
                gn_use.setdefault(u.gn_id, []).append(u)

    def unit_pop(u: R.Unit) -> float | None:
        g = geonames.get(str(u.gn_id))
        if not g or not g.get("pop"):
            return None
        shared = gn_use[u.gn_id]
        if len(shared) == 1:
            return float(g["pop"])
        total_area = sum(x.area for x in shared) or 1
        return g["pop"] * u.area / total_area

    by_key: dict[str, list[R.Region]] = {}
    for r in regions:
        if r.owner:
            by_key.setdefault(r.units[0].key, []).append(r)
    result: dict[str, tuple[float, str]] = {}
    for key, regs in by_key.items():
        owner = regs[0].owner
        main = key == owner or owner_main.get(owner) == key
        target = country_pop[owner] if main else float(ne_props.get(key, {}).get("POP_EST") or 0)
        known_pop, known_area = 0.0, 0.0
        raw: dict[str, tuple[float | None, list[R.Unit]]] = {}
        for r in regs:
            total, missing = 0.0, []
            for u in r.units:
                p = unit_pop(u)
                if p is None:
                    missing.append(u)
                else:
                    total += p
                    known_pop += p
                    known_area += u.area
            raw[r.id] = (total, missing)
        density = known_pop / known_area if known_area > 0 else None
        estimates: dict[str, float] = {}
        for r in regs:
            total, missing = raw[r.id]
            for u in missing:
                if density is not None:
                    total += u.area * density
                else:
                    total += max(u.city_pop * 1.5, u.area)
            estimates[r.id] = max(total, 1.0)
        scale = target / sum(estimates.values()) if target > 0 else 1.0
        for r in regs:
            source = "geonames" if not raw[r.id][1] else "estimate"
            result[r.id] = (round(estimates[r.id] * scale), source)
    return result


def build_map_and_regions(refresh: bool, un: set[str], extras: list[dict], geonames: dict, gn_cities: dict,
                          capital_names: dict[str, str], owner_by_cc: dict[str, str]):
    capital_ru = {e["id"]: e["capital"] for e in extras}
    # Столицы, которых нет в GeoNames от 15 тыс. жителей (Северная Никосия), заданы в tools/extra_states.json.
    manual_cities = [{"name": e["capital"], "name_en": e["capital_city"]["name_en"], "lon": e["capital_city"]["lon"],
                      "lat": e["capital_city"]["lat"], "pop": e["capital_city"]["pop"], "owner": e["id"], "capital": True,
                      "admin1_capital": False, "rank": 0}
                     for e in extras if e.get("capital_city")]
    admin0 = json.loads(fetch(NE_ADMIN0, "ne_10m_admin_0_countries.geojson", refresh))
    admin1 = json.loads(fetch(NE_ADMIN1, "ne_10m_admin_1_states_provinces.geojson", refresh))
    places_raw = json.loads(fetch(NE_PLACES, "ne_10m_populated_places.geojson", refresh))
    owner_of, owner_main, ne_props = resolve_owners(admin0, un, extras)

    places = []
    for f in places_raw["features"]:
        p = f["properties"]
        places.append({
            "name": clean_name(p.get("NAME_RU") or p["NAME"]), "name_en": p["NAME"], "lon": p["LONGITUDE"], "lat": p["LATITUDE"],
            "pop": int(p["POP_MAX"] or 0), "owner": owner_of.get(p["ADM0_A3"]),
            "capital": p["FEATURECLA"] == "Admin-0 capital", "admin1_capital": p["FEATURECLA"] == "Admin-1 capital",
            "rank": int(p["SCALERANK"]),
        })

    units = R.load_units(admin1, owner_of, places)
    type_en = {f["properties"]["adm1_code"]: f["properties"].get("type_en") for f in admin1["features"]}
    for u in units:
        u.type_en = type_en.get(u.code)
    names_ru = json.loads((TOOLS / "region_names_ru.json").read_text(encoding="utf-8"))
    regions = R.group_units(units, owner_main, names_ru)
    name_regions(regions, owner_main, ne_props, geonames)
    print(f"  административных единиц: {len(units)}, игровых регионов: {len(regions)}")

    regions_geo, lines_geo, owners_geo = R.run_mapshaper(units, regions, CACHE / "mapshaper", SIMPLIFY)
    by_id = {r.id: r for r in regions}

    # Регионы: треугольники для заливки, точка подписи.
    map_regions, region_geoms = [], {}
    for f in regions_geo["features"]:
        rid = f["properties"]["gr"]
        r = by_id[rid]
        geom = shape(f["geometry"])
        region_geoms[rid] = geom
        polys = [pg for pg in (build_polygon(rings) for rings in polygons_of(f["geometry"])) if pg]
        if not polys:
            # Крохотный регион (Монако, Ватикан) в проекции схлопывается: рисуем маленький квадрат в центре.
            cx, cy = project(geom.centroid.x, geom.centroid.y)
            d = 0.3
            polys = [{"rings": [[cx - d, cy - d, cx + d, cy - d, cx + d, cy + d, cx - d, cy + d]], "triangles": [0, 1, 2, 0, 2, 3]}]
        lx, ly = R.label_point(geom)
        map_regions.append({"id": rid, "country": r.owner, "territory": r.territory,
                            "label": list(project(lx, ly)), "polygons": polys})
    map_regions.sort(key=lambda m: m["id"])

    # Страны: контур (для подсветки), соседи, точка подписи.
    owner_geoms = {}
    for f in owners_geo["features"]:
        key = f["properties"]["owner"]
        owner_geoms[key] = shape(f["geometry"])
    country_ids = sorted(k for k in owner_geoms if not k.startswith("~"))
    tree = STRtree([owner_geoms[k] for k in country_ids])
    adjacency: dict[str, set[str]] = {k: set() for k in country_ids}
    for k in country_ids:
        for j in tree.query(owner_geoms[k].buffer(1e-3), predicate="intersects"):
            other = country_ids[int(j)]
            if other != k:
                adjacency[k].add(other)
    colors = color_countries(adjacency)

    main_props = {owner: ne_props[code] for owner, code in owner_main.items() if code in ne_props}
    map_countries = []
    for k in country_ids:
        outline = [flat(project_ring(ring)) for poly in polygons_of(owners_geo_geometry(owners_geo, k)) for ring in poly]
        p = main_props.get(k, {})
        if p.get("LABEL_X") is not None:
            label = project(p["LABEL_X"], p["LABEL_Y"])
        else:
            label = project(*R.label_point(owner_geoms[k]))
        map_countries.append({"id": k, "label": list(label), "color": colors[k], "neighbors": sorted(adjacency[k]),
                              "outline": [ring for ring in outline if len(ring) >= 6]})

    territories = [{"id": t, "name": R.NEUTRAL[t][0], "note": R.NEUTRAL[t][1],
                    "label": list(project(*R.label_point(owner_geoms["~" + t])))}
                   for t in sorted(R.NEUTRAL) if "~" + t in owner_geoms]

    # Линии: побережье, границы стран, границы регионов внутри стран.
    lines = {"coast": [], "border": [], "internal": []}
    kind = {"outer": "coast", "owner": "border", "inner": "internal"}
    for f in lines_geo["features"]:
        t = kind.get(f["properties"].get("TYPE"), "internal")
        geom = f["geometry"]
        parts = [geom["coordinates"]] if geom["type"] == "LineString" else geom["coordinates"]
        for part in parts:
            pts = []
            for lon, lat in part:
                pt = project(lon, lat)
                if not pts or pts[-1] != pt:
                    pts.append(pt)
            if len(pts) >= 2:
                lines[t].append(flat(pts))

    ys = [v for m in map_regions for pg in m["polygons"] for ring in pg["rings"] for v in ring[1::2]]
    world = {
        "projection": "equal_earth", "scale": round(SCALE, 6), "width": MAP_WIDTH,
        "min_y": min(ys), "max_y": max(ys), "political_colors": POLITICAL_COLORS,
        "countries": map_countries, "regions": map_regions, "territories": territories, "lines": lines,
    }

    # Города GeoNames (от 15 тыс. жителей): столицы стран и регионов, крупные города, главный город каждого региона.
    region_owner = {r.id: r.owner for r in regions}
    gn_places = geonames_places(gn_cities, places, owner_by_cc, capital_names) + manual_cities
    assigned = R.assign_cities(gn_places, region_geoms, region_owner)
    biggest: dict[str, dict] = {}
    for c in assigned:
        if c["region"] not in biggest or c["pop"] > biggest[c["region"]]["pop"]:
            biggest[c["region"]] = c
    cities, capital_of = [], {}
    for c in assigned:
        named_capital = capital_names.get(c["country"]) in (c["name"], c["name_en"])
        keep = c["capital"] or c["admin1_capital"] or c["pop"] >= 100_000 or biggest[c["region"]] is c or named_capital
        if not keep:
            continue
        x, y = project(c["lon"], c["lat"])
        cities.append({"name": c["name"], "name_en": c["name_en"], "country": c["country"], "region": c["region"], "x": x, "y": y,
                       "population": c["pop"], "capital": c["capital"], "regional_capital": c["admin1_capital"],
                       "rank": c["rank"]})
    # У страны одна столица: та, что в её собственных границах и крупнее. Если столица GeoNames
    # оказалась вне страны, столицей считается город с названием из справочника или крупнейший.
    for country in {c["country"] for c in cities}:
        own = [c for c in cities if c["country"] == country]
        wanted = capital_names.get(country, "")
        by_name = [c for c in own if wanted and wanted in (c["name"], c["name_en"])]
        flagged = [c for c in own if c["capital"]]
        chosen = max(by_name or flagged or own, key=lambda c: c["population"])
        for c in own:
            c["capital"] = c is chosen
        if country in capital_ru:
            chosen["name"] = capital_ru[country]
        capital_of[country] = chosen["name"]
    for c in cities:
        c.pop("name_en", None)
    cities.sort(key=lambda c: (c["country"], c["region"], -c["population"]))
    region_capital = {}
    for c in cities:
        cur = region_capital.get(c["region"])
        better = cur is None or (c["capital"], c["regional_capital"], c["population"]) > (cur["capital"], cur["regional_capital"], cur["population"])
        if better:
            region_capital[c["region"]] = c

    return world, regions, cities, region_capital, capital_of, main_props, owner_main, ne_props


def geonames_places(gn_cities: dict, ne_places: list[dict], owner_by_cc: dict[str, str], capital_names: dict[str, str]) -> list[dict]:
    """Города GeoNames; русское название — из GeoNames или от ближайшего города Natural Earth."""
    ne_tree = STRtree([Point(p["lon"], p["lat"]) for p in ne_places])
    capital_set = {n for n in capital_names.values() if n}
    result = []
    for c in gn_cities.values():
        if c["code"] in ("PPLH", "PPLQ", "PPLW"):  # исторические и заброшенные пункты
            continue
        if c["code"] == "PPLX" and c["name"] not in capital_set and c.get("ru") not in capital_set:
            continue  # районы городов нужны, только если это столица (Северная Никосия)
        name = c.get("ru")
        if not name:
            pt = Point(c["lon"], c["lat"])
            for i in ne_tree.query(pt.buffer(0.15)):
                ne = ne_places[int(i)]
                if ne["pop"] and 0.3 <= c["pop"] / ne["pop"] <= 3:
                    name = ne["name"]
                    break
        result.append({
            "name": clean_name(name or c["name"]), "name_en": c["name"], "lon": c["lon"], "lat": c["lat"], "pop": c["pop"],
            "owner": owner_by_cc.get(c["cc"]), "capital": c["code"] == "PPLC", "admin1_capital": c["code"] == "PPLA", "rank": 0,
        })
    result.sort(key=lambda c: -c["pop"])
    for rank, c in enumerate(result):
        c["rank"] = 0 if c["capital"] else 1 if c["pop"] >= 1_000_000 else 2 if c["pop"] >= 250_000 else 3
    return result


def owners_geo_geometry(owners_geo: dict, key: str) -> dict:
    for f in owners_geo["features"]:
        if f["properties"]["owner"] == key:
            return f["geometry"]
    raise KeyError(key)


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
    parser.add_argument("--refresh", action="store_true", help="скачать Natural Earth и справочник стран заново")
    args = parser.parse_args()

    print("1/4 Справочник стран и снимки источников")
    ref_all = json.loads(fetch(COUNTRIES_REF, "countries_ref.json", args.refresh))
    ref = {c["cca3"]: c for c in ref_all if c.get("unMember") and c["cca3"] != "VAT"}
    extras = json.loads((TOOLS / "extra_states.json").read_text(encoding="utf-8"))["states"]
    un = set(ref)
    wb = load_snapshot("wb_wdi.json")
    imf = load_snapshot("imf_weo.json")
    bis = load_snapshot("bis_policy_rates.json")
    projections = load_snapshot("wb_projections.json")["values"]
    geonames = load_snapshot("geonames_admin.json")["admin"]
    print(f"  членов ООН: {len(un)}, других государств: {len(extras)}")

    print("2/4 Регионы, границы и города")
    gn_cities = load_snapshot("geonames_cities.json")["cities"]
    capital_names = {iso: (c["capital"][0] if c.get("capital") else "") for iso, c in ref.items()}
    capital_names.update({"ISR": "Jerusalem", "BOL": "Sucre", "CIV": "Yamoussoukro", "NLD": "Amsterdam"})
    capital_names.update({e["id"]: e["capital_en"] for e in extras})
    owner_by_cc = {c["cca2"]: iso for iso, c in ref.items()}
    owner_by_cc.update({e["iso2"]: e["id"] for e in extras if e["iso2"]})
    world, regions, cities, region_capital, capitals, ne_by_owner, owner_main, ne_props = build_map_and_regions(
        args.refresh, un, extras, geonames, gn_cities, capital_names, owner_by_cc)

    print("3/4 Показатели стран")
    records = country_records(ref, {c["cca3"]: c for c in ref_all}, extras, ne_by_owner, capitals)
    countries = build_countries(records, wb["values"], imf["values"], bis["rates"])
    check_currencies(countries)
    filled = sum(1 for c in countries for s in c["stats"].values() if not is_measured(s))
    total = sum(len(c["stats"]) for c in countries)
    print(f"  стран: {len(countries)}, значений: {total}, из них оценок: {filled}")

    print("4/4 Население регионов")
    country_pop = {c["id"]: c["stats"]["population"]["value"] for c in countries}
    pops = region_populations(regions, geonames, country_pop, owner_main, ne_props)
    region_records = []
    for r in sorted(regions, key=lambda x: x.id):
        if r.owner is None:
            continue
        pop, source = pops[r.id]
        cap = region_capital.get(r.id)
        region_records.append({
            "id": r.id, "name": r.name, "country": r.owner, "capital": cap["name"] if cap else "",
            "population": pop, "population_source": source, "area_km2": round(sum(u.area for u in r.units)),
            "units": len(r.units),
        })
    estimated = sum(1 for r in region_records if r["population_source"] == "estimate")
    print(f"  регионов: {len(region_records)}, население оценено по плотности: {estimated}")

    built = dt.date.today().isoformat()
    write_json(DATA / "countries.json", {
        "built": built,
        "sources": {
            "wb": "Всемирный банк, World Development Indicators (CC BY 4.0)",
            "imf": "МВФ, World Economic Outlook",
            "bis": "Банк международных расчётов, ключевые ставки центробанков",
            "natural_earth": "Natural Earth (общественное достояние)",
            "fill": "Пропуск заполнен медианой по похожим странам",
            "derived": "Вычислено из других показателей",
        },
        "snapshots": {"wb": wb["retrieved"], "imf": imf["retrieved"], "bis": bis["retrieved"]},
        "countries": countries,
    })
    population, benchmarks = build_population(records, countries, wb["values"], projections)
    print(f"  демография: стран {len(population['countries'])}, с прогнозом {sum(1 for c in population['countries'] if 'path' in c)}")
    write_json(DATA / "population.json", {"built": built, **population}, compact=True)
    (DATA / "benchmarks").mkdir(exist_ok=True)
    write_json(DATA / "benchmarks" / "population.json", benchmarks)
    write_json(DATA / "regions.json", {
        "built": built,
        "sources": {"geonames": "GeoNames (CC BY 4.0), население приведено к населению страны",
                    "estimate": "Оценка по средней плотности населения страны"},
        "regions": region_records,
    })
    write_json(DATA / "map" / "world.json", {"built": built, **world}, compact=True)
    write_json(DATA / "map" / "cities.json", {"built": built, "cities": cities})


if __name__ == "__main__":
    main()
