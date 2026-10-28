"""Игровые регионы: объединение административных единиц Natural Earth 1:10m,
топологическое упрощение границ (mapshaper), города и население регионов.

Используется из tools/build_world.py.
"""

from __future__ import annotations

import collections
import json
import math
import pathlib
import shutil
import subprocess
from dataclasses import dataclass, field

from shapely.geometry import Point, mapping, shape
from shapely.ops import polylabel
from shapely.strtree import STRtree

ROOT = pathlib.Path(__file__).resolve().parent.parent
TOOLS = ROOT / "tools"

# Государства вне ООН, за которые можно играть: код Natural Earth -> код в игре.
EXTRA_STATES = {"TWN": "TWN", "KOS": "XKX", "PSX": "PSE", "SOL": "SOL", "CYN": "CYN"}

# Территории без государства-владельца: на карте нейтральные, в симуляции не участвуют.
NEUTRAL = {
    "SAH": ("Западная Сахара", "Спорная территория: большую часть контролирует Марокко, восток — Сахарская Арабская Демократическая Республика"),
    "KAS": ("Сиачен", "Спорный ледник между Индией и Пакистаном"),
    "VAT": ("Ватикан", "Государство-наблюдатель ООН, в симуляции не участвует"),
}
SKIP = {"ATA"}

# Единицы, которые Natural Earth относит к одной стране, а международно признаны частью другой.
DE_JURE = {("RUS", "Crimea"): "UKR", ("RUS", "Sevastopol"): "UKR"}

# Группы, которые нельзя смешивать с соседями при объединении мелких единиц.
FIXED_GROUPS = {
    "MDA": {
        "Приднестровье": {"Stîngă Nistrului", "Transnistria", "Grigoriopol", "Bender", "Camenca"},
        "Гагаузия": {"Comrat"},
    },
}

# Зависимая территория меньше этой площади — один регион целиком.
DEPENDENCY_SINGLE_KM2 = 300_000
# Страны, где крупные регионы Natural Earth не подходят для игры (смешанные или слишком крупные группы).
REGION_FIELD_IGNORE = {"TWN"}
# Мелкая единица без городов присоединяется к соседу, если её площадь меньше этой доли страны.
ABSORB_SHARE = 0.005


@dataclass
class Unit:
    code: str
    key: str            # группа обработки: основная страна или зависимая территория
    owner: str | None   # страна в игре, None — нейтральная территория
    territory: str | None
    name: str
    name_ru: str
    region: str | None
    gn_id: int
    area: float
    geom: object
    city_pop: int = 0
    main_city: str | None = None
    type_en: str | None = None


@dataclass
class Region:
    id: str
    owner: str | None
    territory: str | None
    name: str
    units: list[Unit] = field(default_factory=list)


def limit_for(area_km2: float) -> int:
    """Сколько регионов разумно иметь стране такой площади: у микрогосударств один, у крупных стран не меньше 8."""
    limit = max(1, round(math.sqrt(area_km2) / 35))
    return max(limit, 8) if area_km2 >= 50_000 else limit


def load_units(admin1: dict, owner_of_adm0: dict[str, str | None], places: list[dict]) -> list[Unit]:
    units = []
    for f in admin1["features"]:
        p = f["properties"]
        adm0 = p["adm0_a3"]
        if adm0 in SKIP:
            continue
        geom = shape(f["geometry"])
        if not geom.is_valid:
            geom = geom.buffer(0)
        de_jure = DE_JURE.get((adm0, p["name"]))
        owner = de_jure or owner_of_adm0.get(adm0)
        territory = adm0 if adm0 in NEUTRAL else None
        if territory:
            owner = None
        elif owner is None:
            print(f"  внимание: единица {p['name']} ({adm0}) без владельца — пропущена")
            continue
        units.append(Unit(
            code=p["adm1_code"],
            key=de_jure or adm0,
            owner=owner,
            territory=territory,
            name=p["name"] or p["adm1_code"],
            name_ru=p.get("name_ru") or p["name"] or "",
            region=p.get("region"),
            gn_id=int(p.get("gn_id") or 0),
            area=float(p.get("area_sqkm") or 0) or geom.area * 111 * 111,
            geom=geom,
        ))

    # Крупнейший город каждой единицы — для имён объединённых регионов и выбора «якоря».
    tree = STRtree([u.geom for u in units])
    for pl in places:
        pt = Point(pl["lon"], pl["lat"])
        for i in tree.query(pt, predicate="intersects"):
            u = units[i]
            if pl["pop"] > u.city_pop:
                u.city_pop = pl["pop"]
                u.main_city = pl["name"]
            break
    return units


def adjacency(units: list[Unit]) -> dict[int, set[int]]:
    tree = STRtree([u.geom for u in units])
    adj: dict[int, set[int]] = {i: set() for i in range(len(units))}
    for i, u in enumerate(units):
        for j in tree.query(u.geom.buffer(1e-4), predicate="intersects"):
            if j != i:
                adj[i].add(int(j))
                adj[int(j)].add(i)
    return adj


def cluster(units: list[Unit], target: int) -> list[list[Unit]]:
    """Склеивает самую маленькую группу с самой маленькой соседней, пока групп больше target."""
    adj = adjacency(units)
    groups: dict[int, set[int]] = {i: {i} for i in range(len(units))}
    owner_of = {i: i for i in range(len(units))}
    area = {i: units[i].area for i in range(len(units))}
    centroid = {i: units[i].geom.centroid for i in range(len(units))}
    while len(groups) > max(1, target):
        g = min(groups, key=lambda k: (area[k], k))
        neighbors = {owner_of[j] for i in groups[g] for j in adj[i]} - {g}
        if neighbors:
            other = min(neighbors, key=lambda k: (area[k], k))
        else:
            other = min((k for k in groups if k != g), key=lambda k: (centroid[g].distance(centroid[k]), k))
        groups[other] |= groups.pop(g)
        area[other] += area.pop(g)
        for i in groups[other]:
            owner_of[i] = other
        centroid[other] = units[max(groups[other], key=lambda i: units[i].area)].geom.centroid
    return [[units[i] for i in sorted(members)] for members in groups.values()]


def group_units(units: list[Unit], owner_main: dict[str, str], names_ru: dict[str, dict[str, str]]) -> list[Region]:
    by_key: dict[str, list[Unit]] = collections.defaultdict(list)
    for u in units:
        by_key[u.key].append(u)

    regions: list[Region] = []
    for key, members in sorted(by_key.items()):
        owner = members[0].owner
        territory = members[0].territory
        area = sum(u.area for u in members)
        groups: list[tuple[str | None, list[Unit]]]

        is_dependency = owner is not None and owner_main.get(owner) not in (key, None) and key != owner
        if territory or (is_dependency and area < DEPENDENCY_SINGLE_KM2):
            groups = [(None, members)]
        else:
            groups = split_country(key, members, area, names_ru.get(key, {}))

        for name, group in groups:
            anchor = max(group, key=lambda u: (u.city_pop, u.area, u.code))
            if name is None:
                if len(group) == 1:
                    # Если у Natural Earth нет русского названия, берём из своего словаря.
                    name = names_ru.get(key, {}).get(group[0].name) if group[0].name_ru in ("", group[0].name) else None
                    name = name or group[0].name_ru or group[0].name
                elif territory:
                    name = NEUTRAL[territory][0]
                else:
                    # Склеенные единицы называются по главному городу.
                    name = anchor.main_city or anchor.name_ru
            regions.append(Region(id=anchor.code, owner=owner, territory=territory, name=name, units=group))
    return regions


def split_country(key: str, members: list[Unit], area: float, names_ru: dict[str, str]) -> list[tuple[str | None, list[Unit]]]:
    limit = limit_for(area)
    fixed: list[tuple[str | None, list[Unit]]] = []
    free = members
    for name, unit_names in FIXED_GROUPS.get(key, {}).items():
        chosen = [u for u in free if u.name in unit_names]
        if chosen:
            fixed.append((name, chosen))
            free = [u for u in free if u.name not in unit_names]

    # Мелкие единицы без городов (нейтральные зоны, островки) уходят к самому крупному соседу.
    hosts, absorbed = absorb_small(free, area) if len(free) > 1 else (free, {})
    free = hosts

    if len(free) + len(fixed) <= limit * 1.5:
        groups = [(None, [u]) for u in free]
    else:
        groups = group_by_region_field(key, free, limit, names_ru) or [(None, g) for g in cluster(free, limit - len(fixed))]
    for _, group in groups:
        for u in list(group):
            group.extend(absorbed.get(u.code, []))
    return fixed + groups


def absorb_small(units: list[Unit], country_area: float) -> tuple[list[Unit], dict[str, list[Unit]]]:
    adj = adjacency(units)
    host_of: dict[int, int] = {}
    for i, u in enumerate(units):
        unnamed = not u.name_ru
        if u.city_pop == 0 and (u.area < ABSORB_SHARE * country_area or unnamed):
            candidates = [j for j in adj[i] if j not in host_of and units[j].area >= u.area and units[j].name_ru]
            if not candidates and unnamed:
                # Безымянный остров уходит к ближайшей единице, даже не соседней.
                named = [j for j in range(len(units)) if j != i and j not in host_of and units[j].name_ru]
                candidates = [min(named, key=lambda j: units[j].geom.distance(u.geom))] if named else []
            if candidates:
                host_of[i] = max(candidates, key=lambda j: (units[j].area, units[j].code))
    absorbed: dict[str, list[Unit]] = {}
    for i, h in host_of.items():
        while h in host_of:
            h = host_of[h]
        absorbed.setdefault(units[h].code, []).append(units[i])
    return [u for i, u in enumerate(units) if i not in host_of], absorbed


def group_by_region_field(key: str, free: list[Unit], limit: int, names_ru: dict[str, str]) -> list[tuple[str | None, list[Unit]]] | None:
    """Крупные регионы Natural Earth (поле region), если они заполнены почти везде и их разумное число."""
    if key in REGION_FIELD_IGNORE:
        return None
    with_region = [u for u in free if u.region]
    if free and len(with_region) >= 0.7 * len(free):
        named: dict[str, list[Unit]] = collections.defaultdict(list)
        for u in with_region:
            named[names_ru.get(u.region, u.region)].append(u)
        centers = {k: max(v, key=lambda u: u.area).geom.centroid for k, v in named.items()}
        for u in free:
            if not u.region:
                nearest = min(centers, key=lambda k: (u.geom.centroid.distance(centers[k]), k))
                named[nearest].append(u)
        g = len(named)
        if max(2, limit / 3) <= g <= max(limit * 1.5, 8):
            return [(name, units) for name, units in sorted(named.items())]
    return None


def run_mapshaper(units: list[Unit], regions: list[Region], workdir: pathlib.Path, simplify: str) -> tuple[dict, dict, dict]:
    """Растворяет единицы в регионы и упрощает границы с сохранением общих рёбер."""
    exe = shutil.which("mapshaper") or str(TOOLS / "node_modules" / ".bin" / "mapshaper")
    if not pathlib.Path(exe).exists() and not shutil.which("mapshaper"):
        raise SystemExit("Нужен mapshaper: выполните «npm install --prefix tools»")
    region_of = {u.code: r for r in regions for u in r.units}
    workdir.mkdir(parents=True, exist_ok=True)
    units_path = workdir / "units.json"
    feats = []
    for u in units:
        r = region_of[u.code]
        feats.append({"type": "Feature", "geometry": mapping(u.geom),
                      "properties": {"gr": r.id, "owner": r.owner or ("~" + (r.territory or ""))}})
    units_path.write_text(json.dumps({"type": "FeatureCollection", "features": feats}))

    regions_path, lines_path, owners_path = workdir / "regions.json", workdir / "lines.json", workdir / "owners.json"
    run = lambda *args: subprocess.run([exe, *args], check=True, capture_output=True, text=True)  # noqa: E731
    # explode перед упрощением: keep-shapes защищает каждый остров, а не только регион целиком.
    run("-i", str(units_path), "-dissolve2", "gr", "copy-fields=owner", "-explode",
        "-simplify", simplify, "weighted", "keep-shapes", "-dissolve2", "gr", "copy-fields=owner", "-clean",
        "-o", str(regions_path), "format=geojson", "precision=0.00001")
    run("-i", str(regions_path), "-lines", "owner", "-o", str(lines_path), "format=geojson", "precision=0.00001")
    run("-i", str(regions_path), "-dissolve2", "owner", "-o", str(owners_path), "format=geojson", "precision=0.00001")
    load = lambda p: json.loads(p.read_text())  # noqa: E731
    return load(regions_path), load(lines_path), load(owners_path)


def label_point(geom) -> tuple[float, float]:
    """Точка подписи внутри самого большого куска региона."""
    poly = max(getattr(geom, "geoms", [geom]), key=lambda g: g.area)
    try:
        p = polylabel(poly, tolerance=0.02)
    except Exception:  # noqa: BLE001 — у вырожденных многоугольников берём внутреннюю точку
        p = poly.representative_point()
    return p.x, p.y


def assign_cities(places: list[dict], region_geoms: dict[str, object], region_owner: dict[str, str | None]) -> list[dict]:
    ids = list(region_geoms)
    tree = STRtree([region_geoms[i] for i in ids])
    result = []
    for pl in places:
        pt = Point(pl["lon"], pl["lat"])
        hit = [ids[i] for i in tree.query(pt, predicate="intersects")]
        own = pl.get("owner")
        if own and (not hit or region_owner[hit[0]] != own):
            # Город у самой границы (Иерусалим) или в море после упрощения (атоллы) относится к своей стране,
            # если её регион совсем рядом.
            radius = 0.05 if hit else 1.0
            near = [ids[i] for i in tree.query(pt.buffer(radius)) if region_owner[ids[i]] == own]
            if near:
                hit = [min(near, key=lambda r: region_geoms[r].distance(pt))]
        if not hit:
            near = [ids[i] for i in tree.query(pt.buffer(0.5))]
            if not near:
                continue
            hit = [min(near, key=lambda r: region_geoms[r].distance(pt))]
        region = hit[0]
        if region_owner[region] is None:
            continue
        result.append({**pl, "region": region, "country": region_owner[region]})
    return result
