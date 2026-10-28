#!/usr/bin/env python3
"""Скачивание первичных источников данных в tools/sources/.

Запускается в GitHub Actions (workflow «Источники данных»), где открыт интернет,
и сохраняет компактные снимки в репозиторий. Скрипт tools/build_world.py
собирает игру уже из этих снимков, без обращения к сети.

  МВФ, World Economic Outlook (DataMapper API)  -> tools/sources/imf_weo.json
  Всемирный банк, World Development Indicators  -> tools/sources/wb_wdi.json
  GeoNames: население и русские названия регионов -> tools/sources/geonames_admin.json

Запуск:  python tools/fetch_sources.py [--only imf,wb,geonames]
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
LAST_YEAR = 2026

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
]

WB_URL = "https://api.worldbank.org/v2/country/all/indicator/{code}?format=json&per_page=20000&date={first}:{last}"
WB_INDICATORS = [
    "SP.POP.TOTL", "NY.GDP.MKTP.CD", "NY.GDP.PCAP.CD", "NY.GDP.PCAP.PP.CD", "NY.GDP.MKTP.KD.ZG",
    "FP.CPI.TOTL.ZG", "NY.GDP.DEFL.KD.ZG", "SL.UEM.TOTL.ZS", "GC.DOD.TOTL.GD.ZS", "GC.TAX.TOTL.GD.ZS",
    "NE.EXP.GNFS.ZS", "NE.IMP.GNFS.ZS", "NV.AGR.TOTL.ZS", "NV.IND.TOTL.ZS", "NV.SRV.TOTL.ZS",
    "SP.DYN.LE00.IN", "SP.DYN.TFRT.IN", "SP.URB.TOTL.IN.ZS", "SP.POP.0014.TO.ZS", "SP.POP.65UP.TO.ZS",
    "SL.TLF.TOTL.IN", "MS.MIL.XPND.GD.ZS", "EG.ELC.ACCS.ZS", "SI.POV.GINI", "NY.GNP.PCAP.CD",
]

NE_ADMIN1 = "https://raw.githubusercontent.com/nvkelso/natural-earth-vector/v5.1.2/geojson/ne_10m_admin_1_states_provinces.geojson"
GEONAMES_ALL = "https://download.geonames.org/export/dump/allCountries.zip"
GEONAMES_ALT = "https://download.geonames.org/export/dump/alternateNamesV2.zip"


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


def fetch_imf() -> None:
    print("МВФ, World Economic Outlook", flush=True)
    data: dict[str, dict[str, dict[str, float]]] = {}
    for ind in IMF_INDICATORS:
        raw = json.loads(get(IMF_URL.format(indicator=ind)))
        series = raw.get("values", {}).get(ind, {})
        data[ind] = {
            country: {y: round(float(v), 4) for y, v in years.items() if v is not None and FIRST_YEAR <= int(y) <= LAST_YEAR}
            for country, years in series.items()
        }
        print(f"  {ind}: стран {len(data[ind])}", flush=True)
        if not data[ind]:
            print(f"  внимание: МВФ вернул пустой ряд {ind}", flush=True)
    if not data.get("GGXWDG_NGDP"):
        raise RuntimeError("МВФ не вернул госдолг")
    write("imf_weo.json", {"source": "IMF World Economic Outlook, DataMapper API", "url": IMF_URL.format(indicator="<код>"),
                           "retrieved": dt.date.today().isoformat(), "values": data})


def fetch_wb() -> None:
    print("Всемирный банк, World Development Indicators", flush=True)
    data: dict[str, dict[str, dict[str, float]]] = {}
    for code in WB_INDICATORS:
        raw = json.loads(get(WB_URL.format(code=code, first=FIRST_YEAR, last=2025)))
        rows = raw[1] if len(raw) > 1 and raw[1] else []
        series: dict[str, dict[str, float]] = {}
        for row in rows:
            iso3 = row.get("countryiso3code")
            if row.get("value") is None or not iso3:
                continue
            series.setdefault(iso3, {})[row["date"]] = round(float(row["value"]), 6)
        data[code] = series
        print(f"  {code}: стран {len(series)}", flush=True)
        if not series:
            raise RuntimeError(f"Всемирный банк вернул пустой ряд {code}")
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

    ru: dict[int, tuple[int, str]] = {}
    path = download(GEONAMES_ALT, TMP / "alternateNamesV2.zip")
    with zipfile.ZipFile(path) as z, z.open("alternateNamesV2.txt") as f:
        for line in io.TextIOWrapper(f, encoding="utf-8"):
            cols = line.rstrip("\n").split("\t")
            if len(cols) < 8 or cols[2] != "ru":
                continue
            gid = int(cols[1])
            if gid not in admin or cols[6] == "1" or cols[7] == "1":  # разговорные и исторические названия не берём
                continue
            rank = 2 if cols[4] == "1" else 1 if cols[5] == "1" else 0
            if gid not in ru or rank > ru[gid][0]:
                ru[gid] = (rank, cols[3])
    for gid, (_, name) in ru.items():
        admin[gid]["ru"] = name
    print(f"  с русским названием: {len(ru)}", flush=True)
    write("geonames_admin.json", {"source": "GeoNames (CC BY 4.0)", "url": GEONAMES_ALL,
                                  "retrieved": dt.date.today().isoformat(),
                                  "admin": {str(k): v for k, v in sorted(admin.items())}})


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--only", default="imf,wb,geonames", help="какие источники скачать, через запятую")
    args = parser.parse_args()
    jobs = {"imf": fetch_imf, "wb": fetch_wb, "geonames": fetch_geonames}
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
