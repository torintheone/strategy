using Godot;
using Politeconomy.Game.UI;
using Politeconomy.Sim.Geo;

namespace Politeconomy.Game.Map;

/// <summary>
/// Подсветка: регион под курсором светлеет, у страны под курсором — светлый контур, у выбранной — золотой
/// контур со свечением. Контуры — сглаженные линии постоянной толщины; сетки кешируются по стране и региону.
/// Подсветка проявляется плавно.
/// </summary>
public partial class MapHighlight : Node2D
{
    const float FadeSeconds = 0.18f;

    readonly Dictionary<string, ArrayMesh> _regionRings = new(StringComparer.Ordinal);
    readonly Dictionary<string, ArrayMesh> _countryRings = new(StringComparer.Ordinal);
    Func<string, Mesh?> _fillOf = _ => null;

    MeshInstance2D _hoverFill = null!;
    MeshInstance2D _selectedFill = null!;
    MapLines _hoverCountry = null!;
    MapLines _hoverRegion = null!;
    MapLines _selectedGlow = null!;
    MapLines _selectedCountry = null!;
    MapLines _selectedRegion = null!;
    float _hoverAlpha;
    float _selectedAlpha;
    string? _hoverKey;
    string? _selectedKey;

    public void Setup(Func<string, Mesh?> fillOf)
    {
        _fillOf = fillOf;
        _selectedFill = new MeshInstance2D { Name = "SelectedFill" };
        _hoverFill = new MeshInstance2D { Name = "HoverFill" };
        AddChild(_selectedFill);
        AddChild(_hoverFill);
        _hoverCountry = Layer("HoverCountry", 1.6f, 1f, new Color(1, 1, 1, 0.45f));
        _selectedGlow = Layer("SelectedGlow", 0f, 14f, new Color(UiTheme.Accent, 0.45f), glow: true);
        _selectedCountry = Layer("SelectedCountry", 2.2f, 1f, UiTheme.Accent);
        _selectedRegion = Layer("SelectedRegion", 1.6f, 1f, UiTheme.AccentLight);
        _hoverRegion = Layer("HoverRegion", 1.3f, 1f, new Color(1, 1, 1, 0.85f));
    }

    MapLines Layer(string name, float width, float feather, Color color, bool glow = false)
    {
        var layer = MapLines.Create(name, [], width, feather, color, glow);
        AddChild(layer);
        return layer;
    }

    public void SetZoom(float zoom)
    {
        foreach (var layer in new[] { _hoverCountry, _hoverRegion, _selectedGlow, _selectedCountry, _selectedRegion })
            layer.SetZoom(zoom);
    }

    public void Set(MapRegion? hoveredRegion, MapCountry? hoveredCountry, MapRegion? selectedRegion, MapCountry? selectedCountry)
    {
        var hoverKey = $"{hoveredRegion?.Id}|{hoveredCountry?.Id}";
        if (hoverKey != _hoverKey)
        {
            _hoverKey = hoverKey;
            _hoverAlpha = 0;
            _hoverCountry.Mesh = hoveredCountry != null && hoveredCountry != selectedCountry ? CountryRings(hoveredCountry) : null;
            _hoverRegion.Mesh = hoveredRegion != null && hoveredRegion != selectedRegion ? RegionRings(hoveredRegion) : null;
            _hoverFill.Mesh = hoveredRegion != null ? _fillOf(hoveredRegion.Id) : null;
        }
        var selectedKey = $"{selectedRegion?.Id}|{selectedCountry?.Id}";
        if (selectedKey != _selectedKey)
        {
            _selectedKey = selectedKey;
            _selectedAlpha = 0;
            var country = selectedCountry != null ? CountryRings(selectedCountry) : null;
            _selectedGlow.Mesh = country;
            _selectedCountry.Mesh = country;
            _selectedRegion.Mesh = selectedRegion != null ? RegionRings(selectedRegion) : null;
            _selectedFill.Mesh = selectedRegion != null ? _fillOf(selectedRegion.Id) : null;
        }
        Apply();
    }

    public override void _Process(double delta)
    {
        if (_hoverAlpha >= 1 && _selectedAlpha >= 1)
            return;
        var step = (float)delta / FadeSeconds;
        _hoverAlpha = Mathf.Min(1, _hoverAlpha + step);
        _selectedAlpha = Mathf.Min(1, _selectedAlpha + step);
        Apply();
    }

    void Apply()
    {
        var hover = Ease(_hoverAlpha);
        var selected = Ease(_selectedAlpha);
        _hoverFill.Modulate = new Color(1, 1, 1, 0.13f * hover);
        _hoverCountry.Modulate = new Color(1, 1, 1, hover);
        _hoverRegion.Modulate = new Color(1, 1, 1, hover);
        _selectedFill.Modulate = new Color(UiTheme.AccentLight, 0.16f * selected);
        _selectedGlow.Modulate = new Color(1, 1, 1, selected);
        _selectedCountry.Modulate = new Color(1, 1, 1, selected);
        _selectedRegion.Modulate = new Color(1, 1, 1, selected);
    }

    static float Ease(float t) => 1 - (1 - t) * (1 - t);

    ArrayMesh RegionRings(MapRegion region)
    {
        if (_regionRings.TryGetValue(region.Id, out var cached))
            return cached;
        var rings = new List<float[]>();
        foreach (var p in region.Polygons)
            for (var r = 0; r < p.RingCount; r++)
                rings.Add(p.Ring(r).ToArray());
        return _regionRings[region.Id] = LineMesh.Build(rings, rings: true);
    }

    ArrayMesh CountryRings(MapCountry country)
    {
        if (_countryRings.TryGetValue(country.Id, out var cached))
            return cached;
        return _countryRings[country.Id] = LineMesh.Build(country.Outline, rings: true);
    }
}
