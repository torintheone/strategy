"""Обработка портретов и иллюстраций событий из art/incoming (часть 8.1, PORTRAITS.md).

Запуск: python tools/process_art.py [--check] [--force]
  --check  только отчёт: что найдено, чего не хватает, какие файлы лишние; ничего не пишет;
  --force  обработать заново даже то, что уже обработано.

Что делает с каждой картинкой, имя которой есть в PORTRAITS.md (регистр и расширение не важны):
  портрет — обрезка до 3:4 (лицо ближе к верхней трети), 600×800; иллюстрация события — обрезка до 16:9, 1280×720;
  выравнивание цвета: баланс белого (серый мир, частично), растяжка уровней, яркость и насыщенность к общим
  значениям и общий лёгкий «тон» игры — чтобы картинки из разных генераций смотрелись одной серией;
  сохранение в WebP: game/assets/portraits/<имя>.webp и game/assets/events/<событие>.webp.
Пишет art/manifest.json: теги каждого портрета (регион, пол, возраст, роль), обработанные иллюстрации, недостающее.
Нужны Pillow и numpy (tools/requirements.txt).
"""
import argparse
import json
import pathlib
import re
import sys

import numpy as np
from PIL import Image, ImageOps

ROOT = pathlib.Path(__file__).resolve().parent.parent
INCOMING = ROOT / "art" / "incoming"
PORTRAITS_OUT = ROOT / "game" / "assets" / "portraits"
EVENTS_OUT = ROOT / "game" / "assets" / "events"
MANIFEST = ROOT / "art" / "manifest.json"
EXTENSIONS = {".png", ".jpg", ".jpeg", ".webp"}

PORTRAIT_SIZE = (600, 800)
EVENT_SIZE = (1280, 720)
# Общие значения, к которым тянется каждая картинка (доля пути — STRENGTH, чтобы не «убить» удачные картинки).
TARGET = {
    "portrait": {"luma": 0.40, "chroma": 0.12},
    "event": {"luma": 0.42, "chroma": 0.14},
}
WHITE_BALANCE_STRENGTH = 0.6
LEVELS_STRENGTH = 0.7
EXPOSURE_STRENGTH = 0.7
SATURATION_STRENGTH = 0.75

ROW = re.compile(r"^\|\s*\d+\s*\|\s*`([^`]+)`\s*\|(.*)\|\s*$")


def read_list():
    """Портреты и иллюстрации из PORTRAITS.md: имя без расширения → описание."""
    text = (ROOT / "PORTRAITS.md").read_text(encoding="utf-8")
    regions = {r["name"]: r["id"] for r in json.loads((ROOT / "art" / "portrait_regions.json").read_text(encoding="utf-8"))["regions"]}
    portraits, events = {}, {}
    section = None
    for line in text.splitlines():
        if line.startswith("## "):
            section = line[3:].strip()
            continue
        m = ROW.match(line)
        if not m:
            continue
        stem = pathlib.Path(m.group(1)).stem.lower()
        cells = [c.strip() for c in m.group(2).split("|")]
        if section == "Портреты":
            region, gender, age, role = cells[0], cells[1], cells[2], cells[3]
            parts = stem.split("_")  # p_<регион>_<роль>_<пол>_<возраст>_<n>
            portraits[stem] = {
                "id": stem,
                "region": regions.get(region, region),
                "gender": "m" if gender.startswith("муж") else "f",
                "age": parts[-2],
                "age_years": age,
                "role": parts[-4],
            }
        elif section == "Иллюстрации событий":
            events[stem] = {"id": stem, "event": cells[0].strip("`")}
    return portraits, events


def crop(img, aspect, top_bias):
    """Обрезка до соотношения aspect (ширина/высота); top_bias — какую долю лишней высоты срезать сверху."""
    w, h = img.size
    if w / h > aspect:
        nw = round(h * aspect)
        left = (w - nw) // 2
        return img.crop((left, 0, left + nw, h))
    nh = round(w / aspect)
    top = round((h - nh) * top_bias)
    return img.crop((0, top, w, top + nh))


def luma(a):
    return 0.2126 * a[..., 0] + 0.7152 * a[..., 1] + 0.0722 * a[..., 2]


def normalize(img, kind):
    """Выравнивание цвета к общим значениям серии. Возвращает картинку и статистику до/после."""
    a = np.asarray(img.convert("RGB"), dtype=np.float32) / 255.0
    before = {"luma": float(luma(a).mean()), "chroma": float(chroma(a).mean())}
    # 1. Баланс белого: серый мир, частично и с ограничением, чтобы не выбелить тёплый портрет.
    means = a.reshape(-1, 3).mean(axis=0)
    gray = means.mean()
    gains = np.clip(gray / np.maximum(means, 1e-4), 0.85, 1.15)
    a = a * (1 + (gains - 1) * WHITE_BALANCE_STRENGTH)
    # 2. Уровни: 1-й и 99-й перцентили яркости — к 0,03 и 0,97.
    y = luma(a)
    lo, hi = np.percentile(y, 1), np.percentile(y, 99)
    if hi - lo > 0.05:
        stretched = (a - lo) / (hi - lo) * 0.94 + 0.03
        a = a + (stretched - a) * LEVELS_STRENGTH
    a = np.clip(a, 0, 1)
    # 3. Яркость: гамма к общей средней яркости.
    target = TARGET[kind]
    mean = max(float(luma(a).mean()), 1e-3)
    gamma = np.clip(np.log(target["luma"]) / np.log(mean), 0.7, 1.4)
    gamma = 1 + (gamma - 1) * EXPOSURE_STRENGTH
    a = np.power(np.clip(a, 1e-6, 1), gamma)
    # 4. Насыщенность (средняя «хрома» — разница между самым ярким и самым тёмным каналом) к общей.
    a = np.clip(a, 0, 1)
    current = max(float(chroma(a).mean()), 1e-3)
    factor = np.clip(target["chroma"] / current, 0.6, 1.4)
    factor = 1 + (factor - 1) * SATURATION_STRENGTH
    y = luma(a)[..., None]
    a = y + (a - y) * factor
    # 5. Общий тон серии: чуть теплее в светах, чуть холоднее в тенях.
    y = luma(a)[..., None]
    a = a + (y - 0.5) * np.array([0.020, 0.004, -0.018], dtype=np.float32)
    a = np.clip(a, 0, 1)
    after = {"luma": float(luma(a).mean()), "chroma": float(chroma(a).mean())}
    return Image.fromarray((a * 255 + 0.5).astype(np.uint8), "RGB"), {"before": before, "after": after}


def chroma(a):
    return a.max(axis=-1) - a.min(axis=-1)


def process(path, kind, out):
    img = ImageOps.exif_transpose(Image.open(path)).convert("RGB")
    if kind == "portrait":
        img = crop(img, PORTRAIT_SIZE[0] / PORTRAIT_SIZE[1], top_bias=0.2)
        size = PORTRAIT_SIZE
    else:
        img = crop(img, EVENT_SIZE[0] / EVENT_SIZE[1], top_bias=0.5)
        size = EVENT_SIZE
    source = img.size
    img = img.resize(size, Image.LANCZOS)
    img, stats = normalize(img, kind)
    out.parent.mkdir(parents=True, exist_ok=True)
    img.save(out, "WEBP", quality=86, method=6)
    stats["source"] = list(source)
    return stats


def main():
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--check", action="store_true")
    parser.add_argument("--force", action="store_true")
    args = parser.parse_args()
    portraits, events = read_list()
    files = {}
    for f in sorted(INCOMING.glob("*")) if INCOMING.exists() else []:
        if f.suffix.lower() in EXTENSIONS:
            files.setdefault(f.stem.lower(), f)
    unknown = sorted(f.name for stem, f in files.items() if stem not in portraits and stem not in events)

    done_portraits, done_events = [], []
    for stem, info in portraits.items():
        if stem not in files:
            continue
        out = PORTRAITS_OUT / f"{stem}.webp"
        entry = dict(info, file=f"res://assets/portraits/{stem}.webp", source=files[stem].name)
        if not args.check and (args.force or not out.exists() or out.stat().st_mtime < files[stem].stat().st_mtime):
            entry["color"] = process(files[stem], "portrait", out)
        done_portraits.append(entry)
    for stem, info in events.items():
        if stem not in files:
            continue
        out = EVENTS_OUT / f"{info['event']}.webp"
        entry = dict(info, file=f"res://assets/events/{info['event']}.webp", source=files[stem].name)
        if not args.check and (args.force or not out.exists() or out.stat().st_mtime < files[stem].stat().st_mtime):
            entry["color"] = process(files[stem], "event", out)
        done_events.append(entry)

    # Чего не хватает: по региону, роли, полу и возрасту.
    have = {p["id"] for p in done_portraits}
    missing = {}
    for stem, info in portraits.items():
        if stem not in have:
            key = f"{info['region']}/{info['role']}/{info['gender']}/{info['age']}"
            missing[key] = missing.get(key, 0) + 1
    missing_events = sorted(info["event"] for stem, info in events.items() if stem not in files)

    print(f"Портретов: {len(done_portraits)} из {len(portraits)}; иллюстраций: {len(done_events)} из {len(events)}.")
    if unknown:
        print("Имена не из списка (переименуйте как в PORTRAITS.md): " + ", ".join(unknown))
    by_region = {}
    for key, n in missing.items():
        region = key.split("/")[0]
        by_region[region] = by_region.get(region, 0) + n
    if by_region:
        print("Не хватает портретов по регионам: " + ", ".join(f"{r} {n}" for r, n in sorted(by_region.items())))
    if missing_events:
        print(f"Не хватает иллюстраций ({len(missing_events)}): " + ", ".join(missing_events))
    if args.check:
        return 0
    MANIFEST.write_text(json.dumps({
        "_comment": "Пишет tools/process_art.py: обработанные портреты с тегами (регион, пол, возраст, роль) и иллюстрации событий; "
                    "missing — каких портретов не хватает (регион/роль/пол/возраст → сколько), unknown — файлы с именами не из списка.",
        "portraits": sorted(done_portraits, key=lambda p: p["id"]),
        "events": sorted(done_events, key=lambda e: e["event"]),
        "missing": dict(sorted(missing.items())),
        "missing_events": missing_events,
        "unknown": unknown,
    }, ensure_ascii=False, indent=1) + "\n", encoding="utf-8")
    print(f"Записано: {MANIFEST.relative_to(ROOT)}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
