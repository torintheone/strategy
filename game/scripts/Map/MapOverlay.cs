using Godot;
using Politeconomy.Game.UI;
using Politeconomy.Sim.Data;
using Politeconomy.Sim.Geo;

namespace Politeconomy.Game.Map;

/// <summary>
/// Подписи стран, регионов и городов и кружки-метки мелких стран. Рисуется в экранных координатах поверх карты,
/// поэтому текст не растягивается при масштабе. Подписи, которые не помещаются или налезают, пропускаются;
/// при приближении подписи проявляются плавно, а не выскакивают.
/// </summary>
public partial class MapOverlay : Control
{
    public const float MarkerRadius = 4.5f;
    /// <summary>Страна рисуется меткой, если на экране она меньше этого размера.</summary>
    public const float MarkerThreshold = 7f;
    /// <summary>С какого масштаба показываются подписи регионов.</summary>
    public const float RegionLabelZoom = 1.2f;

    const float LabelScale = 1f / 9f;
    const int MinLabelSize = 10;
    const int MaxLabelSize = 26;

    WorldMap _map = null!;
    IReadOnlyDictionary<string, string> _names = null!;
    IReadOnlyDictionary<string, string> _regionNames = null!;
    IReadOnlyList<CityRecord> _cities = null!;
    IReadOnlyList<MapCountry> _countriesBySize = null!;
    IReadOnlyList<MapRegion> _regionsBySize = null!;
    readonly List<Rect2> _placed = [];

    /// <summary>Цвет страны на карте для её метки (по стране, которая её контролирует).</summary>
    public IReadOnlyDictionary<string, Color> CountryColors { get; set; } = new Dictionary<string, Color>();
    public string? Hovered { get; set; }
    public string? Selected { get; set; }
    public float Zoom { get; set; } = 1;

    public void Setup(WorldMap map, GameData data)
    {
        _map = map;
        _names = data.Countries.Countries.ToDictionary(c => c.Id, c => c.ShortName);
        _regionNames = data.Regions.Regions.ToDictionary(r => r.Id, r => r.Name);
        _countriesBySize = map.Countries.OrderByDescending(c => c.MainArea).ToList();
        _regionsBySize = map.Regions.Where(r => !r.IsNeutral).OrderByDescending(r => r.Main.Area).ToList();
        _cities = data.Cities.Cities
            .OrderByDescending(c => c.Capital)
            .ThenByDescending(c => c.RegionalCapital)
            .ThenByDescending(c => c.Population)
            .ToList();
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsPreset(LayoutPreset.FullRect);
    }

    /// <summary>Мелкая ли страна при текущем масштабе — тогда она рисуется кружком.</summary>
    public bool IsMarker(MapCountry country) =>
        Math.Max(country.Bounds.Width, country.Bounds.Height) * Zoom < MarkerThreshold;

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
        if (Zoom >= RegionLabelZoom)
            DrawRegionLabels(xf, screen);
        DrawTerritoryLabels(xf, screen);
    }

    void DrawMarkers(Transform2D xf, Rect2 screen)
    {
        foreach (var country in _map.Countries)
        {
            if (!IsMarker(country))
                continue;
            var p = xf * new Vector2(country.LabelX, country.LabelY);
            if (!screen.Grow(10).HasPoint(p))
                continue;
            var fill = CountryColors.TryGetValue(country.Id, out var c) ? c : UiTheme.TextMuted;
            DrawCircle(p, MarkerRadius + 1.5f, new Color(fill, 0.25f), true, -1, true);
            DrawCircle(p, MarkerRadius, fill, true, -1, true);
            var ring = country.Id == Selected ? UiTheme.Accent
                : country.Id == Hovered ? new Color(1, 1, 1, 0.8f)
                : UiTheme.MapOutline;
            DrawArc(p, MarkerRadius, 0, Mathf.Tau, 24, ring, country.Id == Selected ? 2f : 1.2f, true);
        }
    }

    void DrawCountryLabels(Transform2D xf, Rect2 screen)
    {
        // Когда видны регионы, названия стран бледнее, чтобы не спорить с ними.
        var alpha = Mathf.Lerp(0.9f, 0.5f, Fade(RegionLabelZoom, RegionLabelZoom * 1.4f));
        foreach (var country in _countriesBySize)
        {
            var text = _names.GetValueOrDefault(country.Id, country.Id).ToUpperInvariant();
            var size = Math.Min(MaxLabelSize, Mathf.Sqrt(country.MainArea) * Zoom * LabelScale * 0.82f);
            PlaceLabel(xf, screen, text, country.LabelX, country.LabelY, size, country.MainBounds.Width * Zoom * 0.95f,
                UiTheme.MapCountryFont, new Color(UiTheme.TextPrimary, alpha), 5);
        }
    }

    void DrawRegionLabels(Transform2D xf, Rect2 screen)
    {
        var fade = Fade(RegionLabelZoom, RegionLabelZoom * 1.35f);
        foreach (var region in _regionsBySize)
        {
            var size = Math.Min(16, Mathf.Sqrt(region.Main.Area) * Zoom * LabelScale * 0.8f);
            if (size < MinLabelSize - 1.5f)
                break; // дальше регионы ещё мельче
            var text = _regionNames.GetValueOrDefault(region.Id, "");
            PlaceLabel(xf, screen, text, region.LabelX, region.LabelY, size, region.Main.Bounds.Width * Zoom * 0.95f,
                UiTheme.Regular, new Color(UiTheme.TextSecondary, 0.85f * fade), 3);
        }
    }

    void DrawTerritoryLabels(Transform2D xf, Rect2 screen)
    {
        foreach (var t in _map.Territories)
        {
            var region = _map.Regions.FirstOrDefault(r => r.Territory == t.Id);
            if (region == null)
                continue;
            var size = Math.Min(18, Mathf.Sqrt(region.Main.Area) * Zoom * LabelScale);
            PlaceLabel(xf, screen, t.Name, t.LabelX, t.LabelY, size, region.Main.Bounds.Width * Zoom * 0.9f,
                UiTheme.Regular, UiTheme.TextMuted, 3);
        }
    }

    /// <summary>0 ниже масштаба <paramref name="from"/>, 1 выше <paramref name="to"/>, плавно между ними.</summary>
    float Fade(float from, float to) => Mathf.SmoothStep(from, to, Zoom);

    /// <summary>
    /// Подпись по центру точки: уменьшается, пока не влезет в ширину, и пропускается, если налезает на другую.
    /// У самого маленького размера подпись полупрозрачная — так она проявляется при приближении.
    /// </summary>
    void PlaceLabel(Transform2D xf, Rect2 screen, string text, float x, float y, float exactSize, float room, Font font, Color ink, int outline)
    {
        if (exactSize < MinLabelSize - 1.5f || text.Length == 0 || ink.A < 0.02f)
            return;
        ink.A *= Mathf.Clamp((exactSize - (MinLabelSize - 1.5f)) / 3f, 0, 1);
        var size = Math.Max(MinLabelSize, (int)exactSize);
        var center = xf * new Vector2(x, y);
        if (!screen.Grow(400).HasPoint(center))
            return;
        var width = font.GetStringSize(text, HorizontalAlignment.Left, -1, size).X;
        while (width > room && size > MinLabelSize)
        {
            size--;
            width = font.GetStringSize(text, HorizontalAlignment.Left, -1, size).X;
        }
        if (width > room)
            return;
        var height = font.GetHeight(size);
        var rect = new Rect2(center.X - width / 2, center.Y - height / 2, width, height);
        if (!screen.Intersects(rect) || Overlaps(rect))
            return;
        _placed.Add(rect);
        var baseline = new Vector2(rect.Position.X, center.Y + (font.GetAscent(size) - font.GetDescent(size)) / 2);
        DrawStringOutline(font, baseline, text, HorizontalAlignment.Left, -1, size, outline, new Color(UiTheme.MapOutline, UiTheme.MapOutline.A * ink.A));
        DrawString(font, baseline, text, HorizontalAlignment.Left, -1, size, ink);
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
            // Каждый слой городов проявляется плавно на своём масштабе.
            var alpha = Mathf.Max(Mathf.Max(
                    city.Capital ? Fade(0.8f, 1f) : 0,
                    city.Population >= 5_000_000 ? Fade(1.4f, 1.7f) : 0),
                Mathf.Max(Mathf.Max(
                    city.Population >= 1_000_000 ? Fade(2.4f, 2.9f) : 0,
                    city.RegionalCapital ? Fade(3f, 3.6f) : 0),
                    Fade(5f, 6f)));
            if (alpha < 0.03f)
                continue;
            var p = xf * new Vector2(city.X, city.Y);
            if (!screen.HasPoint(p))
                continue;
            var width = font.GetStringSize(city.Name, HorizontalAlignment.Left, -1, size).X;
            var rect = new Rect2(p.X - 4, p.Y - height / 2, width + 12, height);
            if (Overlaps(rect))
                continue;
            _placed.Add(rect);
            Color A(Color c) => new(c, c.A * alpha);
            if (city.Capital)
            {
                DrawCircle(p, 4.2f, A(UiTheme.MapOutline), true, -1, true);
                DrawCircle(p, 3.4f, A(UiTheme.TextPrimary), true, -1, true);
                DrawCircle(p, 1.6f, A(UiTheme.MapOutline), true, -1, true);
            }
            else
            {
                var r = city.RegionalCapital ? 2.6f : 2.1f;
                DrawCircle(p, r + 0.8f, A(UiTheme.MapOutline), true, -1, true);
                DrawCircle(p, r, A(UiTheme.TextSecondary), true, -1, true);
            }
            var baseline = new Vector2(p.X + 7, p.Y - height / 2 + ascent);
            DrawStringOutline(font, baseline, city.Name, HorizontalAlignment.Left, -1, size, 3, A(UiTheme.MapOutline));
            DrawString(font, baseline, city.Name, HorizontalAlignment.Left, -1, size, A(city.Capital ? UiTheme.TextPrimary : UiTheme.TextSecondary));
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
