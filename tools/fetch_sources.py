#!/usr/bin/env python3
"""Скачивание первичных источников данных в tools/sources/.

Запускается в GitHub Actions (workflow «Источники данных»), где открыт интернет,
и сохраняет компактные снимки в репозиторий. Скрипт tools/build_world.py
собирает игру уже из этих снимков, без обращения к сети.

  МВФ, World Economic Outlook (DataMapper API)  -> tools/sources/imf_weo.json
  Всемирный банк, World Development Indicators  -> tools/sources/wb_wdi.json
  GeoNames: население и русские названия регионов -> tools/sources/geonames_admin.json
  GeoNames: города от 15 тыс. жителей              -> tools/sources/geonames_cities.json
  BIS: ключевые ставки центробанков                -> tools/sources/bis_policy_rates.json

Запуск:  python tools/fetch_sources.py [--only imf,wb,bis,geonames]
"""

from __future__ import annotations

import argparse
import csv
import datetime as dt
import io
import json
import pathlib
import sys
import time
import urllib.request
import zipfile

ROOT = pathlib.Path(__file__).resolve().parent.parent
OUT = ROOT / "tools" / "sources"
TMP = ROOT / "tools" / ".cache"

HEADERS = {
    "User-Agent": "Mozilla/5.0 (X11; Linux x86_64) Politeconomy-data-fetch/1.0 (+https://github.com)",
    "Accept": "application/json, text/plain, */*",
}

FIRST_YEAR = 2010
LAST_YEAR = 2030  # МВФ даёт прогноз на 5 лет вперёд: по нему калибруются тренды модели

IMF_URL = "https://www.imf.org/external/datamapper/api/v1/{indicator}"
IMF_INDICATORS = [
    "GGXWDG_NGDP",   # госдолг (general government gross debt), % ВВП
    "GGXCNL_NGDP",   # сальдо бюджета (net lending/borrowing), % ВВП
    "rev",           # доходы бюджета, % ВВП (в DataMapper код короткий)
    "exp",           # расходы бюджета, % ВВП
    "NGDPD",         # ВВП, млрд долл.
    "NGDPDPC",       # ВВП на душу, долл.
    "PPPPC",         # ВВП на душу по ППС
    "NGDP_RPCH",     # рост реального ВВП, %
    "PCPIPCH",       # инфляция, %
    "LUR",           # безработица, %
    "LP",            # население, млн
    "BCA_NGDPD",     # сальдо текущего счёта, % ВВП
    "GGXONLB_NGDP",  # первичное сальдо бюджета (без процентов по долгу), % ВВП
    "NID_NGDP",      # инвестиции, % ВВП
    "NGSD_NGDP",     # национальные сбережения, % ВВП
]
IMF_CORE = {"GGXWDG_NGDP", "NGDP_RPCH", "PCPIPCH"}
# В DataMapper у части рядов WEO другие коды. Если ряд пуст, ищем замену в списке индикаторов по словам в названии.
IMF_SEARCH = {
    "GGXONLB_NGDP": ["primary"],
    "NID_NGDP": ["investment"],
    "NGSD_NGDP": ["saving"],
}
IMF_INDICATORS_URL = "https://www.imf.org/external/datamapper/api/v1/indicators"

WB_URL = "https://api.worldbank.org/v2/country/all/indicator/{code}?format=json&per_page=20000&date={first}:{last}"
WB_INDICATORS = [
    "SP.POP.TOTL", "NY.GDP.MKTP.CD", "NY.GDP.PCAP.CD", "NY.GDP.PCAP.PP.CD", "NY.GDP.MKTP.KD.ZG",
    "FP.CPI.TOTL.ZG", "NY.GDP.DEFL.KD.ZG", "SL.UEM.TOTL.ZS", "GC.DOD.TOTL.GD.ZS", "GC.TAX.TOTL.GD.ZS",
    "NE.EXP.GNFS.ZS", "NE.IMP.GNFS.ZS", "NV.AGR.TOTL.ZS", "NV.IND.TOTL.ZS", "NV.SRV.TOTL.ZS",
    "SP.DYN.LE00.IN", "SP.DYN.TFRT.IN", "SP.URB.TOTL.IN.ZS", "SP.POP.0014.TO.ZS", "SP.POP.65UP.TO.ZS",
    "SL.TLF.TOTL.IN", "MS.MIL.XPND.GD.ZS", "EG.ELC.ACCS.ZS", "SI.POV.GINI", "NY.GNP.PCAP.CD",
    # структура экономики и занятости — для калибровки отраслей
    "NV.IND.MANF.ZS", "NY.GDP.TOTL.RT.ZS", "NY.GDP.PETR.RT.ZS", "NY.GDP.NGAS.RT.ZS", "NY.GDP.COAL.RT.ZS",
    "NY.GDP.MINR.RT.ZS", "NE.GCF.TOTL.ZS", "NE.CON.PRVT.ZS", "NE.CON.GOVT.ZS", "SL.AGR.EMPL.ZS",
    "SL.IND.EMPL.ZS", "SL.SRV.EMPL.ZS", "ST.INT.RCPT.CD", "SP.POP.GROW", "FI.RES.TOTL.CD",
    "PA.NUS.FCRF", "FR.INR.RINR", "FR.INR.LEND", "GC.XPN.INTP.RV.ZS",
]
WB_CORE = set(WB_INDICATORS[:25])  # без них не собрать мир; остальные — для калибровки, их можно оценить

BIS_URL = "https://stats.bis.org/api/v1/data/WS_CBPOL/M./all?startPeriod={start}&detail=dataonly"

NE_ADMIN1 = "https://raw.githubusercontent.com/nvkelso/natural-earth-vector/v5.1.2/geojson/ne_10m_admin_1_states_provinces.geojson"
GEONAMES_ALL = "https://download.geonames.org/export/dump/allCountries.zip"
GEONAMES_ALT = "https://download.geonames.org/export/dump/alternateNamesV2.zip"
GEONAMES_CITIES = "https://download.geonames.org/export/dump/cities15000.zip"


def get(url: str, timeout: int = 300, attempts: int = 4) -> bytes:
    last: Exception | None = None
    for i in range(attempts):
        try:
            req = urllib.request.Request(url, headers=HEADERS)
            with urllib.request.urlopen(req, timeout=timeout) as r:
                return r.read()
        except Exception as e:  # noqa: BLE001 — повторяем при любом сетевом сбое
            last = e
            wait = 5 * 2 ** i
            print(f"  не получилось ({e}), повтор через {wait} с", flush=True)
            time.sleep(wait)
    raise RuntimeError(f"Не удалось скачать {url}: {last}")


def download(url: str, path: pathlib.Path) -> pathlib.Path:
    if path.exists():
        return path
    path.parent.mkdir(parents=True, exist_ok=True)
    print(f"  скачиваю {url}", flush=True)
    tmp = path.with_suffix(path.suffix + ".part")
    req = urllib.request.Request(url, headers=HEADERS)
    with urllib.request.urlopen(req, timeout=600) as r, tmp.open("wb") as f:
        while chunk := r.read(1 << 20):
            f.write(chunk)
    tmp.rename(path)
    return path


def write(name: str, obj: dict) -> None:
    OUT.mkdir(parents=True, exist_ok=True)
    path = OUT / name
    with path.open("w", encoding="utf-8", newline="\n") as f:
        json.dump(obj, f, ensure_ascii=False, separators=(",", ":"), sort_keys=True)
        f.write("\n")
    print(f"  записан {path.relative_to(ROOT)} ({path.stat().st_size // 1024} КБ)", flush=True)


def imf_series(code: str) -> dict[str, dict[str, float]]:
    raw = json.loads(get(IMF_URL.format(indicator=code)))
    series = raw.get("values", {}).get(code, {})
    return {
        country: {y: round(float(v), 4) for y, v in years.items() if v is not None and FIRST_YEAR <= int(y) <= LAST_YEAR}
        for country, years in series.items()
    }


def fetch_imf() -> None:
    print("МВФ, World Economic Outlook", flush=True)
    data: dict[str, dict[str, dict[str, float]]] = {}
    aliases: dict[str, str] = {}
    catalog: dict[str, dict] | None = None
    for ind in IMF_INDICATORS:
        data[ind] = imf_series(ind)
        print(f"  {ind}: стран {len(data[ind])}", flush=True)
        if data[ind] or ind not in IMF_SEARCH:
            continue
        if catalog is None:
            catalog = json.loads(get(IMF_INDICATORS_URL)).get("indicators", {})
        words = IMF_SEARCH[ind]
        candidates = [code for code, info in catalog.items()
                      if all(w in (info.get("label") or "").lower() for w in words)]
        for code in candidates:
            print(f"    кандидат {code}: {catalog[code].get('label')} ({catalog[code].get('unit')}, {catalog[code].get('source')})", flush=True)
        for code in candidates:
            unit = (catalog[code].get("unit") or "").lower()
            if "gdp" not in unit:
                continue
            series = imf_series(code)
            if len(series) >= 100:
                data[ind] = series
                aliases[ind] = code
                print(f"  {ind} ← {code}: стран {len(series)}", flush=True)
                break
        if not data[ind]:
            print(f"  внимание: МВФ вернул пустой ряд {ind}", flush=True)
    for ind in IMF_CORE:
        if not data.get(ind):
            raise RuntimeError(f"МВФ не вернул {ind}")
    write("imf_weo.json", {"source": "IMF World Economic Outlook, DataMapper API", "url": IMF_URL.format(indicator="<код>"),
                           "retrieved": dt.date.today().isoformat(), "aliases": aliases, "values": data})


def fetch_bis() -> None:
    """Ключевые ставки центробанков (BIS, около 40 стран, включая ФРС и ЕЦБ) за последние месяцы."""
    print("BIS: ключевые ставки", flush=True)
    req = urllib.request.Request(BIS_URL.format(start="2025-01"),
                                 headers={**HEADERS, "Accept": "application/vnd.sdmx.data+csv;version=1.0.0"})
    with urllib.request.urlopen(req, timeout=120) as r:
        text = r.read().decode("utf-8")
    rows = list(csv.DictReader(io.StringIO(text)))
    if not rows:
        raise RuntimeError("BIS вернул пустой ответ")
    print(f"  столбцы: {list(rows[0].keys())}", flush=True)
    rates: dict[str, dict[str, float]] = {}
    for row in rows:
        area = row.get("REF_AREA") or row.get("REF_AREA:Reference area", "").split(":")[0]
        period = row.get("TIME_PERIOD") or row.get("TIME_PERIOD:Time period or range", "")
        value = row.get("OBS_VALUE") or row.get("OBS_VALUE:Observation Value", "")
        if area and period and value not in ("", "NaN"):
            rates.setdefault(area, {})[period] = float(value)
    print(f"  стран и валютных союзов: {len(rates)}", flush=True)
    write("bis_policy_rates.json", {"source": "BIS, Central bank policy rates (WS_CBPOL)", "url": BIS_URL.format(start="2025-01"),
                                    "retrieved": dt.date.today().isoformat(), "rates": rates})


def fetch_wb() -> None:
    print("Всемирный банк, World Development Indicators", flush=True)
    data: dict[str, dict[str, dict[str, float]]] = {}
    for code in WB_INDICATORS:
        series: dict[str, dict[str, float]] = {}
        for attempt in range(3):
            # API иногда отвечает сообщением об ошибке вместо данных — тогда ждём и спрашиваем снова
            raw = json.loads(get(WB_URL.format(code=code, first=FIRST_YEAR, last=2025)))
            rows = raw[1] if isinstance(raw, list) and len(raw) > 1 and raw[1] else []
            for row in rows:
                iso3 = row.get("countryiso3code")
                if row.get("value") is None or not iso3:
                    continue
                series.setdefault(iso3, {})[row["date"]] = round(float(row["value"]), 6)
            if series:
                break
            print(f"  {code}: пустой ответ {str(raw)[:200]}, повтор", flush=True)
            time.sleep(10 * (attempt + 1))
        data[code] = series
        print(f"  {code}: стран {len(series)}", flush=True)
        if not series:
            if code in WB_CORE:
                raise RuntimeError(f"Всемирный банк вернул пустой ряд {code}")
            print(f"  внимание: Всемирный банк вернул пустой ряд {code}", flush=True)
    write("wb_wdi.json", {"source": "World Bank, World Development Indicators API (CC BY 4.0)",
                          "url": WB_URL.format(code="<код>", first=FIRST_YEAR, last=2025),
                          "retrieved": dt.date.today().isoformat(), "values": data})


def fetch_geonames() -> None:
    print("GeoNames: регионы", flush=True)
    ne = json.loads(get(NE_ADMIN1))
    wanted = {int(f["properties"]["gn_id"]) for f in ne["features"] if f["properties"].get("gn_id") and int(f["properties"]["gn_id"]) > 0}
    print(f"  регионов Natural Earth с кодом GeoNames: {len(wanted)}", flush=True)

    admin: dict[int, dict] = {}
    path = download(GEONAMES_ALL, TMP / "allCountries.zip")
    with zipfile.ZipFile(path) as z, z.open("allCountries.txt") as f:
        for line in io.TextIOWrapper(f, encoding="utf-8"):
            cols = line.rstrip("\n").split("\t")
            if len(cols) < 15 or cols[6] != "A":
                continue
            gid = int(cols[0])
            code = cols[7]
            if code == "ADM1" or gid in wanted:
                admin[gid] = {"name": cols[1], "cc": cols[8], "code": code, "a1": cols[10], "a2": cols[11],
                              "pop": int(cols[14] or 0)}
    print(f"  административных единиц: {len(admin)}", flush=True)

    cities: dict[int, dict] = {}
    path = download(GEONAMES_CITIES, TMP / "cities15000.zip")
    with zipfile.ZipFile(path) as z, z.open("cities15000.txt") as f:
        for line in io.TextIOWrapper(f, encoding="utf-8"):
            cols = line.rstrip("\n").split("\t")
            if len(cols) < 15 or cols[6] != "P":
                continue
            cities[int(cols[0])] = {"name": cols[1], "lat": round(float(cols[4]), 4), "lon": round(float(cols[5]), 4),
                                    "cc": cols[8], "code": cols[7], "a1": cols[10], "pop": int(cols[14] or 0)}
    print(f"  городов: {len(cities)}", flush=True)

    ru: dict[int, tuple[int, str]] = {}
    path = download(GEONAMES_ALT, TMP / "alternateNamesV2.zip")
    with zipfile.ZipFile(path) as z, z.open("alternateNamesV2.txt") as f:
        for line in io.TextIOWrapper(f, encoding="utf-8"):
            cols = line.rstrip("\n").split("\t")
            if len(cols) < 8 or cols[2] != "ru":
                continue
            gid = int(cols[1])
            if (gid not in admin and gid not in cities) or cols[6] == "1" or cols[7] == "1":  # разговорные и исторические не берём
                continue
            rank = 2 if cols[4] == "1" else 1 if cols[5] == "1" else 0
            if gid not in ru or rank > ru[gid][0]:
                ru[gid] = (rank, cols[3])
    for gid, (_, name) in ru.items():
        (admin.get(gid) or cities[gid])["ru"] = name
    print(f"  с русским названием: {len(ru)}", flush=True)
    write("geonames_admin.json", {"source": "GeoNames (CC BY 4.0)", "url": GEONAMES_ALL,
                                  "retrieved": dt.date.today().isoformat(),
                                  "admin": {str(k): v for k, v in sorted(admin.items())}})
    write("geonames_cities.json", {"source": "GeoNames (CC BY 4.0), города от 15 тыс. жителей", "url": GEONAMES_CITIES,
                                   "retrieved": dt.date.today().isoformat(),
                                   "cities": {str(k): v for k, v in sorted(cities.items())}})


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--only", default="imf,wb,bis,geonames", help="какие источники скачать, через запятую")
    args = parser.parse_args()
    jobs = {"imf": fetch_imf, "wb": fetch_wb, "bis": fetch_bis, "geonames": fetch_geonames}
    failed = []
    for name in args.only.split(","):
        try:
            jobs[name.strip()]()
        except Exception as e:  # noqa: BLE001 — один источник не должен ронять остальные
            print(f"ОШИБКА {name}: {e}", file=sys.stderr, flush=True)
            failed.append(name)
    if failed:
        print(f"Не удалось: {', '.join(failed)}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
