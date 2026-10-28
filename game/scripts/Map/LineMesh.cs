using Godot;

namespace Politeconomy.Game.Map;

/// <summary>
/// Сетка для линий постоянной толщины (шейдер map_line): каждая точка ломаной даёт две вершины,
/// которые шейдер раздвигает поперёк линии. В UV — направление сдвига с поправкой на угол стыка,
/// в цвете — сторона. Замкнутые кольца (первая точка равна последней) стыкуются без шва.
/// </summary>
public static class LineMesh
{
    const float MiterLimit = 0.35f;

    /// <param name="rings">Все линии — замкнутые кольца, даже если последняя точка не повторяет первую.</param>
    public static ArrayMesh Build(IEnumerable<float[]> lines, bool rings = false)
    {
        var vertices = new List<Vector2>();
        var directions = new List<Vector2>();
        var sides = new List<Color>();
        var indices = new List<int>();
        var points = new List<Vector2>();
        foreach (var line in lines)
        {
            points.Clear();
            for (var i = 0; i + 1 < line.Length; i += 2)
            {
                var p = new Vector2(line[i], line[i + 1]);
                if (points.Count == 0 || points[^1].DistanceSquaredTo(p) > 1e-6f)
                    points.Add(p);
            }
            var repeated = points.Count > 3 && points[0].DistanceSquaredTo(points[^1]) < 1e-6f;
            if (repeated)
                points.RemoveAt(points.Count - 1);
            var closed = repeated || (rings && points.Count > 2);
            if (points.Count < 2)
                continue;
            var n = points.Count;
            var first = vertices.Count;
            for (var i = 0; i < n; i++)
            {
                Vector2? prev = i > 0 ? points[i - 1] : closed ? points[n - 1] : null;
                Vector2? next = i < n - 1 ? points[i + 1] : closed ? points[0] : null;
                var dir = Direction(prev, points[i], next);
                vertices.Add(points[i]);
                directions.Add(dir);
                sides.Add(new Color(0, 0, 0));
                vertices.Add(points[i]);
                directions.Add(dir);
                sides.Add(new Color(1, 0, 0));
            }
            var segments = closed ? n : n - 1;
            for (var i = 0; i < segments; i++)
            {
                var a = first + 2 * i;
                var b = first + 2 * ((i + 1) % n);
                indices.Add(a);
                indices.Add(a + 1);
                indices.Add(b);
                indices.Add(a + 1);
                indices.Add(b + 1);
                indices.Add(b);
            }
        }
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = vertices.ToArray();
        arrays[(int)Mesh.ArrayType.TexUV] = directions.ToArray();
        arrays[(int)Mesh.ArrayType.Color] = sides.ToArray();
        arrays[(int)Mesh.ArrayType.Index] = indices.ToArray();
        var mesh = new ArrayMesh();
        if (vertices.Count > 0)
            mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        return mesh;
    }

    /// <summary>Куда сдвигать вершину: нормаль к линии, на стыке — биссектриса, удлинённая под угол.</summary>
    static Vector2 Direction(Vector2? prev, Vector2 p, Vector2? next)
    {
        if (prev is null)
            return Normal(next!.Value - p);
        if (next is null)
            return Normal(p - prev.Value);
        var n1 = Normal(p - prev.Value);
        var n2 = Normal(next.Value - p);
        var miter = (n1 + n2).Normalized();
        if (miter == Vector2.Zero)
            return n1;
        var cos = miter.Dot(n1);
        return miter / Mathf.Max(cos, MiterLimit);
    }

    static Vector2 Normal(Vector2 v)
    {
        var d = v.Normalized();
        return new Vector2(-d.Y, d.X);
    }
}
