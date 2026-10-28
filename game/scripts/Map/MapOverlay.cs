using Godot;
using Politeconomy.Game.UI;
using Politeconomy.Sim.Data;
using Politeconomy.Sim.Geo;

namespace Politeconomy.Game.Map;

/// <summary>
/// Подписи стран и городов и кружки-метки мелких стран. Рисуется в экранных координатах поверх карты,
/// поэтому текст не растягивается при масштабе. Подписи, которые не помещаются или налезают, пропускаются.
/// </summary>
public partial class MapOverlay : Control
{
    public const float MarkerRadius = 4.5f;
    /// <summary>Страна рисуется меткой, если на экране она меньше этого размера.</summary>
    public const float MarkerThreshold = 7f;

    const float LabelScale = 1f / 9f;
    const int MinLabelSize = 10;
    const int MaxLabelSize = 26;

    WorldMap _map = null!;
    IReadOnlyDictionary<string, string> _names = null!;
    IReadOnlyList<CityRecord> _cities = null!;
    IReadOnlyList<MapRegion> _bySize = null!;
    readonly List<Rect2> _placed = [];

    public IReadOnlyDictionary<string, Color> Colors { get; set; } = new Dictionary<string, Color>();
    public string? Hovered { get; set; }
    public string? Selected { get; set; }
    public float Zoom { get; set; } = 1;

    public void Setup(WorldMap map, GameData data)
    {
        _map = map;
        _names = data.Countries.Countries.ToDictionary(c => c.Id, c => c.ShortName);
        _bySize = map.Regions.OrderByDescending(r => r.Main.Area).ToList();
        _cities = data.Cities.Cities
            .OrderByDescending(c => c.Capital)
            .ThenByDescending(c => c.Population)
            .ToList();
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsPreset(LayoutPreset.FullRect);
    }

    /// <summary>Мелкая ли страна при текущем масштабе — тогда она рисуется кружком.</summary>
    public bool IsMarker(MapRegion region) =>
        region.IsCountry && Math.Max(region.Bounds.Width, region.Bounds.Height) * Zoom < MarkerThreshold;

    public override void _Draw()
    {
        if (_map == null)
            return;
        var xf = GetViewport().CanvasTransform;
        var screen = GetViewportRect();
        _placed.Clear();

        DrawMarkers(xf, screen);
        DrawCountryLabels(xf, screen);
        DrawCities(xf, screen);
    }

    void DrawMarkers(Transform2D xf, Rect2 screen)
    {
        foreach (var region in _map.Regions)
        {
            if (!IsMarker(region))
                continue;
            var p = xf * new Vector2(region.LabelX, region.LabelY);
            if (!screen.Grow(10).HasPoint(p))
                continue;
            var fill = Colors.TryGetValue(region.Id, out var c) ? c : UiTheme.TextMuted;
            DrawCircle(p, MarkerRadius, fill);
            var ring = region.Id == Selected ? UiTheme.Accent
                : region.Id == Hovered ? new Color(1, 1, 1, 0.8f)
                : UiTheme.MapOutline;
            DrawArc(p, MarkerRadius, 0, Mathf.Tau, 24, ring, region.Id == Selected ? 2f : 1.2f, true);
        }
    }

    void DrawCountryLabels(Transform2D xf, Rect2 screen)
    {
        var font = UiTheme.SemiBold;
        foreach (var region in _bySize)
        {
            var main = region.Main;
            var size = (int)Math.Min(MaxLabelSize, Mathf.Sqrt(main.Area) * Zoom * LabelScale);
            if (size < MinLabelSize)
                continue;
            var text = region.IsCountry ? _names.GetValueOrDefault(region.Id, region.Id) : region.Name ?? region.Id;
            var room = main.Bounds.Width * Zoom * 0.9f;
            var width = font.GetStringSize(text, HorizontalAlignment.Left, -1, size).X;
            while (width > room && size > MinLabelSize)
            {
                size--;
                width = font.GetStringSize(text, HorizontalAlignment.Left, -1, size).X;
            }
            if (width > room)
                continue;
            var center = xf * new Vector2(region.LabelX, region.LabelY);
            var height = font.GetHeight(size);
            var rect = new Rect2(center.X - width / 2, center.Y - height / 2, width, height);
            if (!screen.Intersects(rect) || Overlaps(rect))
                continue;
            _placed.Add(rect);
            var baseline = new Vector2(rect.Position.X, center.Y + (font.GetAscent(size) - font.GetDescent(size)) / 2);
            var ink = region.IsCountry ? new Color(UiTheme.TextPrimary, 0.88f) : UiTheme.TextMuted;
            DrawStringOutline(font, baseline, text, HorizontalAlignment.Left, -1, size, 4, UiTheme.MapOutline);
            DrawString(font, baseline, text, HorizontalAlignment.Left, -1, size, ink);
        }
    }

    void DrawCities(Transform2D xf, Rect2 screen)
    {
        if (Zoom < 0.8f)
            return;
        var font = UiTheme.Regular;
        const int size = 12;
        var ascent = font.GetAscent(size);
        var height = font.GetHeight(size);
        foreach (var city in _cities)
        {
            var visible = (city.Capital && Zoom >= 0.8f)
                || (city.Population >= 5_000_000 && Zoom >= 1.4f)
                || (city.Population >= 1_000_000 && Zoom >= 2.4f)
                || Zoom >= 4f;
            if (!visible)
                continue;
            var p = xf * new Vector2(city.X, city.Y);
            if (!screen.HasPoint(p))
                continue;
            var width = font.GetStringSize(city.Name, HorizontalAlignment.Left, -1, size).X;
            var rect = new Rect2(p.X - 4, p.Y - height / 2, width + 12, height);
            if (Overlaps(rect))
                continue;
            _placed.Add(rect);
            if (city.Capital)
            {
                DrawCircle(p, 3.5f, UiTheme.TextPrimary);
                DrawCircle(p, 1.8f, UiTheme.Ocean);
            }
            else
            {
                DrawCircle(p, 2.5f, UiTheme.TextSecondary);
            }
            var baseline = new Vector2(p.X + 7, p.Y - height / 2 + ascent);
            DrawStringOutline(font, baseline, city.Name, HorizontalAlignment.Left, -1, size, 3, UiTheme.MapOutline);
            DrawString(font, baseline, city.Name, HorizontalAlignment.Left, -1, size, city.Capital ? UiTheme.TextPrimary : UiTheme.TextSecondary);
        }
    }

    bool Overlaps(Rect2 rect)
    {
        var grown = rect.Grow(2);
        foreach (var r in _placed)
            if (r.Intersects(grown))
                return true;
        return false;
    }
}
