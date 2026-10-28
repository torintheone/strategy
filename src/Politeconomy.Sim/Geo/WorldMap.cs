using Politeconomy.Sim.Data;

namespace Politeconomy.Sim.Geo;

public readonly record struct Rect(float MinX, float MinY, float MaxX, float MaxY)
{
    public float Width => MaxX - MinX;
    public float Height => MaxY - MinY;
    public bool Contains(float x, float y) => x >= MinX && x <= MaxX && y >= MinY && y <= MaxY;

    public static Rect Of(ReadOnlySpan<float> xy)
    {
        float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
        for (var i = 0; i + 1 < xy.Length; i += 2)
        {
            minX = Math.Min(minX, xy[i]);
            maxX = Math.Max(maxX, xy[i]);
            minY = Math.Min(minY, xy[i + 1]);
            maxY = Math.Max(maxY, xy[i + 1]);
        }
        return new Rect(minX, minY, maxX, maxY);
    }

    public Rect Union(Rect o) => new(Math.Min(MinX, o.MinX), Math.Min(MinY, o.MinY), Math.Max(MaxX, o.MaxX), Math.Max(MaxY, o.MaxY));
}

/// <summary>Один многоугольник: кольца (внешнее и дыры) и треугольники для заливки.</summary>
public sealed class MapPolygon
{
    /// <summary>Все вершины колец подряд: x0, y0, x1, y1, …</summary>
    public required float[] Vertices { get; init; }
    /// <summary>Где в <see cref="Vertices"/> начинается каждое кольцо (в вершинах, не в числах).</summary>
    public required int[] RingStarts { get; init; }
    public required int[] Triangles { get; init; }
    public required Rect Bounds { get; init; }
    public required float Area { get; init; }

    public int RingCount => RingStarts.Length;

    public ReadOnlySpan<float> Ring(int index)
    {
        var start = RingStarts[index];
        var end = index + 1 < RingStarts.Length ? RingStarts[index + 1] : Vertices.Length / 2;
        return Vertices.AsSpan(start * 2, (end - start) * 2);
    }

    public bool Contains(float x, float y)
    {
        if (!Bounds.Contains(x, y))
            return false;
        var v = Vertices;
        var t = Triangles;
        for (var i = 0; i + 2 < t.Length; i += 3)
            if (InTriangle(x, y, v[t[i] * 2], v[t[i] * 2 + 1], v[t[i + 1] * 2], v[t[i + 1] * 2 + 1], v[t[i + 2] * 2], v[t[i + 2] * 2 + 1]))
                return true;
        return false;
    }

    static bool InTriangle(float px, float py, float ax, float ay, float bx, float by, float cx, float cy)
    {
        var d1 = (px - bx) * (ay - by) - (ax - bx) * (py - by);
        var d2 = (px - cx) * (by - cy) - (bx - cx) * (py - cy);
        var d3 = (px - ax) * (cy - ay) - (cx - ax) * (py - ay);
        var neg = d1 < 0 || d2 < 0 || d3 < 0;
        var pos = d1 > 0 || d2 > 0 || d3 > 0;
        return !(neg && pos);
    }
}

/// <summary>Регион на карте: часть страны или нейтральная территория.</summary>
public sealed class MapRegion
{
    public required string Id { get; init; }
    /// <summary>Страна-владелец. Пусто у нейтральных территорий.</summary>
    public string? Country { get; init; }
    public string? Territory { get; init; }
    public required float LabelX { get; init; }
    public required float LabelY { get; init; }
    public required IReadOnlyList<MapPolygon> Polygons { get; init; }
    public required Rect Bounds { get; init; }
    /// <summary>Самый большой многоугольник — по нему решаем, поместится ли подпись.</summary>
    public required MapPolygon Main { get; init; }
    public required float Area { get; init; }

    public bool IsNeutral => Country is null;

    public bool Contains(float x, float y)
    {
        if (!Bounds.Contains(x, y))
            return false;
        foreach (var p in Polygons)
            if (p.Contains(x, y))
                return true;
        return false;
    }
}

/// <summary>Страна на карте: контур (без треугольников — заливка идёт по регионам), подпись, цвет.</summary>
public sealed class MapCountry
{
    public required string Id { get; init; }
    public required float LabelX { get; init; }
    public required float LabelY { get; init; }
    public required int ColorIndex { get; init; }
    public required IReadOnlyList<string> Neighbors { get; init; }
    public required IReadOnlyList<float[]> Outline { get; init; }
    public required Rect Bounds { get; init; }
    /// <summary>Площадь и рамка самого большого куска страны — для размера подписи.</summary>
    public required float MainArea { get; init; }
    public required Rect MainBounds { get; init; }
    public required IReadOnlyList<MapRegion> Regions { get; init; }
}

public sealed record MapTerritory(string Id, string Name, string Note, float LabelX, float LabelY);

/// <summary>Геометрия карты в координатах проекции Equal Earth (ось y вниз) и поиск региона под точкой.</summary>
public sealed class WorldMap
{
    public required float Width { get; init; }
    public required float MinY { get; init; }
    public required float MaxY { get; init; }
    public required IReadOnlyList<MapRegion> Regions { get; init; }
    public required IReadOnlyList<MapCountry> Countries { get; init; }
    public required IReadOnlyList<MapTerritory> Territories { get; init; }
    public required IReadOnlyList<float[]> CoastLines { get; init; }
    public required IReadOnlyList<float[]> BorderLines { get; init; }
    public required IReadOnlyList<float[]> InternalLines { get; init; }

    readonly Dictionary<string, MapRegion> _regions = new(StringComparer.Ordinal);
    readonly Dictionary<string, MapCountry> _countries = new(StringComparer.Ordinal);

    public float MinX => -Width / 2;
    public float MaxX => Width / 2;
    public float Height => MaxY - MinY;

    public MapRegion? Region(string id)
    {
        if (_regions.Count == 0)
            foreach (var r in Regions)
                _regions[r.Id] = r;
        return _regions.GetValueOrDefault(id);
    }

    public MapCountry? Country(string id)
    {
        if (_countries.Count == 0)
            foreach (var c in Countries)
                _countries[c.Id] = c;
        return _countries.GetValueOrDefault(id);
    }

    public MapTerritory? Territory(string id) => Territories.FirstOrDefault(t => t.Id == id);

    /// <summary>Регион под точкой или null, если там море.</summary>
    public MapRegion? FindRegionAt(float x, float y)
    {
        foreach (var r in Regions)
            if (r.Contains(x, y))
                return r;
        return null;
    }

    /// <summary>
    /// Мелкая страна, чья метка (кружок на месте подписи) ближе <paramref name="radius"/> к точке.
    /// <paramref name="isMarker"/> решает, рисуется ли страна сейчас меткой.
    /// </summary>
    public MapCountry? FindMarkerAt(float x, float y, float radius, Func<MapCountry, bool> isMarker)
    {
        MapCountry? best = null;
        var bestDist = radius * radius;
        foreach (var c in Countries)
        {
            if (!isMarker(c))
                continue;
            var dx = c.LabelX - x;
            var dy = c.LabelY - y;
            var d = dx * dx + dy * dy;
            if (d <= bestDist)
            {
                best = c;
                bestDist = d;
            }
        }
        return best;
    }

    public static WorldMap From(WorldMapFile file)
    {
        var regions = file.Regions.Select(Region).ToList();
        var byCountry = regions.Where(r => r.Country != null).GroupBy(r => r.Country!).ToDictionary(g => g.Key, g => (IReadOnlyList<MapRegion>)g.ToList());
        var countries = new List<MapCountry>();
        foreach (var c in file.Countries)
        {
            var outline = c.Outline.Select(r => r.ToArray()).ToList();
            var own = byCountry.GetValueOrDefault(c.Id) ?? [];
            if (own.Count == 0)
                throw new DataException($"У страны {c.Id} на карте нет ни одного региона");
            var bounds = own.Skip(1).Aggregate(own[0].Bounds, (acc, r) => acc.Union(r.Bounds));
            var main = outline.Count > 0 ? outline.MaxBy(RingArea)! : own.MaxBy(r => r.Area)!.Main.Vertices;
            countries.Add(new MapCountry
            {
                Id = c.Id,
                LabelX = (float)c.Label[0],
                LabelY = (float)c.Label[1],
                ColorIndex = c.Color,
                Neighbors = c.Neighbors,
                Outline = outline,
                Bounds = bounds,
                MainArea = RingArea(main),
                MainBounds = Rect.Of(main),
                Regions = own,
            });
        }
        return new WorldMap
        {
            Width = (float)file.Width,
            MinY = (float)file.MinY,
            MaxY = (float)file.MaxY,
            Regions = regions,
            Countries = countries,
            Territories = file.Territories.Select(t => new MapTerritory(t.Id, t.Name, t.Note, (float)t.Label[0], (float)t.Label[1])).ToList(),
            CoastLines = file.Lines.Coast.Select(l => l.ToArray()).ToList(),
            BorderLines = file.Lines.Border.Select(l => l.ToArray()).ToList(),
            InternalLines = file.Lines.Internal.Select(l => l.ToArray()).ToList(),
        };
    }

    /// <summary>Площадь кольца по формуле шнурования.</summary>
    public static float RingArea(float[] ring)
    {
        double sum = 0;
        var n = ring.Length / 2;
        for (var i = 0; i < n; i++)
        {
            var j = (i + 1) % n;
            sum += (double)ring[i * 2] * ring[j * 2 + 1] - (double)ring[j * 2] * ring[i * 2 + 1];
        }
        return (float)Math.Abs(sum / 2);
    }

    static MapRegion Region(MapRegionRecord r)
    {
        var polygons = r.Polygons.Select(Polygon).ToList();
        if (polygons.Count == 0)
            throw new DataException($"У региона {r.Id} на карте нет ни одного многоугольника");
        var bounds = polygons.Skip(1).Aggregate(polygons[0].Bounds, (acc, p) => acc.Union(p.Bounds));
        return new MapRegion
        {
            Id = r.Id,
            Country = r.Country,
            Territory = r.Territory,
            LabelX = (float)r.Label[0],
            LabelY = (float)r.Label[1],
            Polygons = polygons,
            Bounds = bounds,
            Main = polygons.MaxBy(p => p.Area)!,
            Area = polygons.Sum(p => p.Area),
        };
    }

    static MapPolygon Polygon(MapPolygonRecord record)
    {
        var total = record.Rings.Sum(r => r.Count);
        var vertices = new float[total];
        var starts = new int[record.Rings.Count];
        var offset = 0;
        for (var i = 0; i < record.Rings.Count; i++)
        {
            starts[i] = offset / 2;
            var ring = record.Rings[i];
            for (var j = 0; j < ring.Count; j++)
                vertices[offset + j] = ring[j];
            offset += ring.Count;
        }
        var triangles = record.Triangles.ToArray();
        var area = 0f;
        for (var i = 0; i + 2 < triangles.Length; i += 3)
        {
            float ax = vertices[triangles[i] * 2], ay = vertices[triangles[i] * 2 + 1];
            float bx = vertices[triangles[i + 1] * 2], by = vertices[triangles[i + 1] * 2 + 1];
            float cx = vertices[triangles[i + 2] * 2], cy = vertices[triangles[i + 2] * 2 + 1];
            area += Math.Abs((bx - ax) * (cy - ay) - (cx - ax) * (by - ay)) / 2;
        }
        return new MapPolygon
        {
            Vertices = vertices,
            RingStarts = starts,
            Triangles = triangles,
            Bounds = Rect.Of(vertices),
            Area = area,
        };
    }
}
