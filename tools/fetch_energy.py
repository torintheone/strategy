#!/usr/bin/env python3
"""
Снимок данных об электроэнергетике (Our World in Data, energy-data, CC BY 4.0) в tools/sources/owid_energy.json.

Берёт по странам за 2015–2025 годы: спрос на электричество на человека, выработку и спрос, доли выработки по
источникам (уголь, газ, нефть, атом, гидро, остальные возобновляемые — ветер, солнце, биотопливо, геотермальные)
и чистый импорт электричества (доля спроса). Дальше снимок читает tools/build_world.py.

Запуск: python tools/fetch_energy.py
"""

import csv
import datetime
import io
import json
import pathlib
import urllib.request

URL = "https://raw.githubusercontent.com/owid/energy-data/master/owid-energy-data.csv"
OUT = pathlib.Path(__file__).resolve().parent / "sources" / "owid_energy.json"
YEARS = range(2015, 2026)
COLUMNS = [
    "electricity_demand_per_capita", "electricity_generation", "electricity_demand",
    "coal_share_elec", "gas_share_elec", "oil_share_elec", "nuclear_share_elec", "hydro_share_elec",
    "renewables_share_elec", "net_elec_imports_share_demand",
]
# Страны, у которых в OWID нет кода ISO.
NAMES = {"Kosovo": "XKX"}


def main() -> None:
    print(f"скачиваю {URL}")
    with urllib.request.urlopen(URL, timeout=300) as r:
        text = r.read().decode("utf-8")
    values: dict[str, dict[str, dict[str, float]]] = {c: {} for c in COLUMNS + ["other_renewables_share_elec_total"]}
    for row in csv.DictReader(io.StringIO(text)):
        iso = NAMES.get(row["country"], row["iso_code"])
        if len(iso) != 3 or int(row["year"]) not in YEARS:
            continue
        year = row["year"]
        # Если у страны за год есть хоть одна доля выработки, пустые доли — это ноль, а не пропуск.
        shares = [c for c in COLUMNS if c.endswith("_share_elec")]
        known = any(row[c] != "" for c in shares)
        for c in COLUMNS:
            if row[c] != "":
                values[c].setdefault(iso, {})[year] = round(float(row[c]), 4)
            elif known and c in shares:
                values[c].setdefault(iso, {})[year] = 0.0
        # Ветер, солнце, биотопливо и прочие возобновляемые = все возобновляемые − гидро.
        if row["renewables_share_elec"] != "" and row["hydro_share_elec"] != "":
            other = max(0.0, float(row["renewables_share_elec"]) - float(row["hydro_share_elec"]))
            values["other_renewables_share_elec_total"].setdefault(iso, {})[year] = round(other, 4)
    snapshot = {
        "retrieved": datetime.date.today().isoformat(),
        "source": "Our World in Data, energy-data (Ember, Energy Institute; CC BY 4.0)",
        "url": URL,
        "values": values,
    }
    OUT.write_text(json.dumps(snapshot, ensure_ascii=False, separators=(",", ":")), encoding="utf-8")
    print(f"записан {OUT.name}: стран {len(values['electricity_generation'])}, {OUT.stat().st_size // 1024} КБ")


if __name__ == "__main__":
    main()
