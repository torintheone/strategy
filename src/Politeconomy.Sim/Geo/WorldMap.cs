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

/// <summary>Страна или нейтральная территория на карте.</summary>
public sealed class MapRegion
{
    public required string Id { get; init; }
    public required bool IsCountry { get; init; }
    /// <summary>Название для нейтральной территории (у стран название берётся из countries.json).</summary>
    public string? Name { get; init; }
    public string? Note { get; init; }
    public required float LabelX { get; init; }
    public required float LabelY { get; init; }
    public required int ColorIndex { get; init; }
    public required IReadOnlyList<string> Neighbors { get; init; }
    public required IReadOnlyList<MapPolygon> Polygons { get; init; }
    public required Rect Bounds { get; init; }
    /// <summary>Самый большой многоугольник — по нему решаем, поместится ли подпись.</summary>
    public required MapPolygon Main { get; init; }
    public required float Area { get; init; }

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

/// <summary>Геометрия карты в координатах проекции Equal Earth (ось y вниз) и поиск страны под точкой.</summary>
public sealed class WorldMap
{
    public required float Width { get; init; }
    public required float MinY { get; init; }
    public required float MaxY { get; init; }
    public required IReadOnlyList<MapRegion> Regions { get; init; }

    public float MinX => -Width / 2;
    public float MaxX => Width / 2;
    public float Height => MaxY - MinY;

    public IEnumerable<MapRegion> Countries => Regions.Where(r => r.IsCountry);

    public MapRegion? ById(string id) => Regions.FirstOrDefault(r => r.Id == id);

    /// <summary>
    /// Регион под точкой. Если точка ни в каком многоугольнике, но рядом с подписью мелкой страны
    /// (ближе <paramref name="markerRadius"/>), возвращается эта страна — так можно попасть в островные государства.
    /// </summary>
    public MapRegion? FindAt(float x, float y, float markerRadius = 0)
    {
        foreach (var r in Regions)
            if (r.Contains(x, y))
                return r;
        if (markerRadius <= 0)
            return null;
        MapRegion? best = null;
        var bestDist = markerRadius * markerRadius;
        foreach (var r in Regions)
        {
            if (!r.IsCountry)
                continue;
            var dx = r.LabelX - x;
            var dy = r.LabelY - y;
            var d = dx * dx + dy * dy;
            if (d <= bestDist)
            {
                best = r;
                bestDist = d;
            }
        }
        return best;
    }

    public static WorldMap From(WorldMapFile file)
    {
        var regions = new List<MapRegion>();
        foreach (var c in file.Countries)
            regions.Add(Region(c.Id, true, null, null, c.Label, c.Color, c.Neighbors, c.Polygons));
        foreach (var t in file.Territories)
            regions.Add(Region(t.Id, false, t.Name, t.Note, t.Label, -1, [], t.Polygons));
        return new WorldMap
        {
            Width = (float)file.Width,
            MinY = (float)file.MinY,
            MaxY = (float)file.MaxY,
            Regions = regions,
        };
    }

    static MapRegion Region(string id, bool isCountry, string? name, string? note, IReadOnlyList<double> label, int color,
        IReadOnlyList<string> neighbors, IReadOnlyList<MapPolygonRecord> records)
    {
        var polygons = records.Select(Polygon).ToList();
        if (polygons.Count == 0)
            throw new DataException($"У региона {id} на карте нет ни одного многоугольника");
        var bounds = polygons.Skip(1).Aggregate(polygons[0].Bounds, (acc, p) => acc.Union(p.Bounds));
        var main = polygons.MaxBy(p => p.Area)!;
        return new MapRegion
        {
            Id = id,
            IsCountry = isCountry,
            Name = name,
            Note = note,
            LabelX = (float)label[0],
            LabelY = (float)label[1],
            ColorIndex = color,
            Neighbors = neighbors,
            Polygons = polygons,
            Bounds = bounds,
            Main = main,
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
