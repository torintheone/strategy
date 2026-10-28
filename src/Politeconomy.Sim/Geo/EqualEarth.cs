namespace Politeconomy.Sim.Geo;

/// <summary>
/// Проекция Equal Earth (Šavrič, Patterson, Jenny, 2018) — та же, что в tools/build_world.py.
/// Ось y направлена вниз; <paramref name="scale"/> — из map/world.json.
/// </summary>
public static class EqualEarth
{
    const double A1 = 1.340264, A2 = -0.081106, A3 = 0.000893, A4 = 0.003796;
    static readonly double M = Math.Sqrt(3) / 2;

    public static (float X, float Y) Project(double lon, double lat, double scale)
    {
        var lam = lon * Math.PI / 180;
        var th = Math.Asin(M * Math.Sin(lat * Math.PI / 180));
        var th2 = th * th;
        var th6 = th2 * th2 * th2;
        var x = 2 * Math.Sqrt(3) * lam * Math.Cos(th) / (3 * (9 * A4 * th6 * th2 + 7 * A3 * th6 + 3 * A2 * th2 + A1));
        var y = th * (A1 + A2 * th2 + th6 * (A3 + A4 * th2));
        return ((float)(x * scale), (float)(-y * scale));
    }

    /// <summary>Контур всей проекции: меридиан 180° сверху вниз и обратно по −180°.</summary>
    public static float[] Outline(double scale, int steps = 90)
    {
        var points = new List<float>();
        for (var i = 0; i <= steps; i++)
        {
            var (x, y) = Project(180, 90 - 180.0 * i / steps, scale);
            points.Add(x);
            points.Add(y);
        }
        for (var i = 0; i <= steps; i++)
        {
            var (x, y) = Project(-180, -90 + 180.0 * i / steps, scale);
            points.Add(x);
            points.Add(y);
        }
        return points.ToArray();
    }

    /// <summary>Сетка параллелей и меридианов с шагом <paramref name="step"/> градусов, ломаными.</summary>
    public static List<float[]> Graticule(double scale, double step = 15)
    {
        var lines = new List<float[]>();
        for (var lat = -90 + step; lat < 90; lat += step)
        {
            var line = new List<float>();
            for (var lon = -180.0; lon <= 180; lon += 2)
            {
                var (x, y) = Project(lon, lat, scale);
                line.Add(x);
                line.Add(y);
            }
            lines.Add(line.ToArray());
        }
        for (var lon = -180 + step; lon < 180; lon += step)
        {
            var line = new List<float>();
            for (var lat = -90.0; lat <= 90; lat += 2)
            {
                var (x, y) = Project(lon, lat, scale);
                line.Add(x);
                line.Add(y);
            }
            lines.Add(line.ToArray());
        }
        return lines;
    }
}
