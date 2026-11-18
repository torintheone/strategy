#!/usr/bin/env python3
"""
Карта войны: соседи регионов по суше и через узкие проливы, доля гор в регионе (data/terrain.json).

Регионы берутся из data/map/world.json (уже в проекции карты), горные хребты — из Natural Earth 1:10m
(ne_10m_geography_regions_polys, класс Range/mtn; скачивается в tools/.cache). Соседи по суше — регионы, чьи
границы сходятся (с допуском LAND_GAP: упрощённые контуры соседей расходятся на сотни метров). Через море —
регионы, до которых не больше SEA_GAP (около 150 км: Тайваньский пролив, Ла-Манш, Сахалин), не больше
SEA_LINKS ближайших своих и столько же чужих. Запуск: python3 -I tools/build_terrain.py. MODEL.md, «Армии и войны».
"""
import datetime
import json
import pathlib
import sys

from shapely.geometry import Polygon, MultiPolygon, shape
from shapely.ops import unary_union
from shapely.strtree import STRtree
from shapely.validation import make_valid

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
import build_world as W  # noqa: E402  (проекция и загрузка Natural Earth)

NE_GEOGRAPHY = W.NE_BASE + "ne_10m_geography_regions_polys.geojson"
# Единица карты — около 4,9 км на экваторе (ширина карты 8192 на 360°).
LAND_GAP = 1.0
SEA_GAP = 30.0
SEA_LINKS = 4


def region_geometry(region: dict):
    polys = []
    for p in region["polygons"]:
        rings = [[(r[i], r[i + 1]) for i in range(0, len(r), 2)] for r in p["rings"]]
        if len(rings[0]) < 3:
            continue
        polys.append(Polygon(rings[0], [r for r in rings[1:] if len(r) >= 3]))
    geom = make_valid(MultiPolygon(polys) if len(polys) > 1 else polys[0])
    return geom.buffer(0)


def project_geometry(geom):
    def ring(coords):
        return [W.project(lon, lat) for lon, lat in coords]

    if geom.geom_type == "Polygon":
        return Polygon(ring(geom.exterior.coords), [ring(r.coords) for r in geom.interiors]).buffer(0)
    return unary_union([project_geometry(g) for g in geom.geoms])


def main() -> None:
    world = json.loads((W.DATA / "map" / "world.json").read_text())
    regions = [r for r in world["regions"] if r.get("country")]
    geoms = [region_geometry(r) for r in regions]
    ids = [r["id"] for r in regions]
    countries = [r["country"] for r in regions]
    tree = STRtree(geoms)

    raw = json.loads(W.fetch(NE_GEOGRAPHY, "ne_10m_geography_regions_polys.geojson", refresh=False))
    ranges = [project_geometry(shape(f["geometry"])) for f in raw["features"] if f["properties"].get("FEATURECLA") == "Range/mtn"]
    mountains = unary_union([g for g in ranges if not g.is_empty])
    mountain_tree = STRtree([mountains])

    out = []
    for i, g in enumerate(geoms):
        near = g.buffer(LAND_GAP)
        land = sorted(ids[j] for j in tree.query(near) if j != i and near.intersects(geoms[j]))
        sea_candidates = sorted((g.distance(geoms[j]), ids[j], countries[j]) for j in tree.query(g.buffer(SEA_GAP))
                                if j != i and ids[j] not in land)
        sea_candidates = [c for c in sea_candidates if c[0] <= SEA_GAP]
        # До SEA_LINKS ближайших своих и столько же чужих: иначе свои острова заслоняли бы пролив к соседу.
        own = [rid for _, rid, c in sea_candidates if c == countries[i]][:SEA_LINKS]
        foreign = [rid for _, rid, c in sea_candidates if c != countries[i]][:SEA_LINKS]
        sea = sorted(own + foreign)
        share = 0.0
        if len(mountain_tree.query(g)) and g.area > 0:
            share = round(g.intersection(mountains).area / g.area, 2)
        out.append({"id": ids[i], "land": land, "sea": sea, "mountains": share})

    # Связи симметричны: если A видит B через море, то и B видит A.
    by_id = {r["id"]: r for r in out}
    for r in out:
        for k in ("land", "sea"):
            for other in r[k]:
                lst = by_id[other][k]
                if r["id"] not in lst and r["id"] not in by_id[other]["land"]:
                    lst.append(r["id"])
    for r in out:
        r["sea"] = sorted(set(r["sea"]) - set(r["land"]))
        r["land"] = sorted(set(r["land"]))

    result = {
        "built": datetime.date.today().isoformat(),
        "source": "data/map/world.json; Natural Earth 1:10m geography regions (Range/mtn), v5.1.2",
        "regions": sorted(out, key=lambda r: r["id"]),
    }
    path = W.DATA / "terrain.json"
    lines = ["{", f' "built": {json.dumps(result["built"])},', f' "source": {json.dumps(result["source"], ensure_ascii=False)},', ' "regions": [']
    for n, r in enumerate(result["regions"]):
        lines.append("  " + json.dumps(r, ensure_ascii=False) + ("," if n < len(result["regions"]) - 1 else ""))
    lines += [" ]", "}"]
    path.write_text("\n".join(lines) + "\n")
    land_links = sum(len(r["land"]) for r in out) // 2
    sea_links = sum(len(r["sea"]) for r in out) // 2
    print(f"Регионов {len(out)}, соседств по суше {land_links}, через море {sea_links}, "
          f"с горами больше 30% — {sum(r['mountains'] > 0.3 for r in out)}")


if __name__ == "__main__":
    main()
