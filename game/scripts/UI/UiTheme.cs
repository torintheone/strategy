using Godot;

namespace Politeconomy.Game.UI;

/// <summary>
/// Цвета, шрифты и стили интерфейса: тёмное «стекло» поверх карты, мягкие скругления, один тёплый
/// золотой акцент. Цифры набраны моноширинными (табличными) цифрами, чтобы не дрожали при обновлении.
/// </summary>
public static class UiTheme
{
    public static readonly Color Ocean = Color.FromHtml("#0b0f15");
    public static readonly Color Panel = Color.FromHtml("#0e1218e6");
    public static readonly Color PanelRaised = Color.FromHtml("#1a2029");
    public static readonly Color PanelBorder = Color.FromHtml("#ffffff17");
    public static readonly Color Hairline = Color.FromHtml("#ffffff10");
    public static readonly Color ButtonBg = Color.FromHtml("#ffffff0d");
    public static readonly Color ButtonHover = Color.FromHtml("#ffffff1a");
    public static readonly Color ButtonPressed = Color.FromHtml("#e0ae522e");
    public static readonly Color TextPrimary = Color.FromHtml("#eceff3");
    public static readonly Color TextSecondary = Color.FromHtml("#b7bec9");
    public static readonly Color TextMuted = Color.FromHtml("#7f8896");
    public static readonly Color Accent = Color.FromHtml("#e0ae52");
    public static readonly Color AccentLight = Color.FromHtml("#f2cf85");
    public static readonly Color Critical = Color.FromHtml("#e0605a");

    // Карта
    public static readonly Color MapGraticule = Color.FromHtml("#96b4dc0f");
    public static readonly Color MapRimGlow = Color.FromHtml("#5a8cc82e");
    public static readonly Color MapRim = Color.FromHtml("#8cafd747");
    public static readonly Color MapRegionBorder = Color.FromHtml("#080b1047");
    public static readonly Color MapCoast = Color.FromHtml("#06090ebf");
    public static readonly Color MapBorder = Color.FromHtml("#06080cd0");
    public static readonly Color MapOutline = Color.FromHtml("#080b10d9");

    public static Font Regular { get; private set; } = null!;
    public static Font Medium { get; private set; } = null!;
    public static Font SemiBold { get; private set; } = null!;
    public static Font SerifBold { get; private set; } = null!;
    /// <summary>Цифры одинаковой ширины: значения в панелях не «прыгают», когда меняются.</summary>
    public static Font Numbers { get; private set; } = null!;
    /// <summary>Названия стран на карте: заглавные с разрядкой.</summary>
    public static Font MapCountryFont { get; private set; } = null!;

    public static Theme Build()
    {
        var plex = GD.Load<FontFile>("res://assets/fonts/IBMPlexSans-Variable.ttf");
        plex.SubpixelPositioning = TextServer.SubpixelPositioning.OneQuarter;
        plex.Hinting = TextServer.Hinting.Light;
        Regular = Weight(plex, 400);
        Medium = Weight(plex, 500);
        SemiBold = Weight(plex, 600);
        Numbers = Weight(plex, 500, tabular: true);
        MapCountryFont = Weight(plex, 600, spacing: 2);
        SerifBold = GD.Load<FontFile>("res://assets/fonts/PTSerif-Bold.ttf");

        var t = new Theme { DefaultFont = Regular, DefaultFontSize = 15 };

        t.SetStylebox("panel", "PanelContainer", Box(Panel, PanelBorder, 12, 14, 12));
        t.SetStylebox("panel", "Panel", Box(Panel, PanelBorder, 12, 0, 0));
        t.SetColor("font_color", "Label", TextPrimary);

        t.SetStylebox("normal", "Button", Box(ButtonBg, null, 8, 11, 6));
        t.SetStylebox("hover", "Button", Box(ButtonHover, null, 8, 11, 6));
        t.SetStylebox("pressed", "Button", Box(ButtonPressed, null, 8, 11, 6));
        t.SetStylebox("hover_pressed", "Button", Box(ButtonPressed, null, 8, 11, 6));
        t.SetStylebox("disabled", "Button", Box(ButtonBg, null, 8, 11, 6));
        t.SetStylebox("focus", "Button", new StyleBoxEmpty());
        t.SetColor("font_color", "Button", TextSecondary);
        t.SetColor("font_hover_color", "Button", TextPrimary);
        t.SetColor("font_pressed_color", "Button", AccentLight);
        t.SetColor("font_hover_pressed_color", "Button", AccentLight);
        t.SetColor("font_focus_color", "Button", TextSecondary);
        t.SetColor("font_disabled_color", "Button", TextMuted);
        t.SetFontSize("font_size", "Button", 14);
        t.SetFont("font", "Button", Medium);

        var tooltip = Box(PanelRaised, PanelBorder, 10, 14, 11);
        tooltip.ShadowColor = new Color(0, 0, 0, 0.45f);
        tooltip.ShadowSize = 14;
        tooltip.ShadowOffset = new Vector2(0, 4);
        t.SetStylebox("panel", "TooltipPanel", tooltip);
        t.SetColor("font_color", "TooltipLabel", TextPrimary);
        t.SetFontSize("font_size", "TooltipLabel", 14);
        t.SetFont("font", "TooltipLabel", Regular);
        t.SetConstant("line_spacing", "TooltipLabel", 3);

        t.SetStylebox("separator", "HSeparator", new StyleBoxLine { Color = Hairline, Thickness = 1 });
        t.SetConstant("separation", "HSeparator", 16);
        t.SetStylebox("separator", "VSeparator", new StyleBoxLine { Color = Hairline, Thickness = 1, Vertical = true });
        t.SetConstant("separation", "VSeparator", 16);

        t.SetStylebox("scroll", "VScrollBar", new StyleBoxEmpty { ContentMarginLeft = 4, ContentMarginRight = 0 });
        t.SetStylebox("grabber", "VScrollBar", Box(new Color(1, 1, 1, 0.12f), null, 3, 2, 0));
        t.SetStylebox("grabber_highlight", "VScrollBar", Box(new Color(1, 1, 1, 0.22f), null, 3, 2, 0));
        t.SetStylebox("grabber_pressed", "VScrollBar", Box(new Color(1, 1, 1, 0.3f), null, 3, 2, 0));
        return t;
    }

    static FontVariation Weight(FontFile file, int weight, bool tabular = false, int spacing = 0)
    {
        var server = TextServerManager.GetPrimaryInterface();
        var font = new FontVariation
        {
            BaseFont = file,
            VariationOpentype = new Godot.Collections.Dictionary { { server.NameToTag("wght"), weight } },
        };
        if (tabular)
            font.OpentypeFeatures = new Godot.Collections.Dictionary { { server.NameToTag("tnum"), 1 } };
        if (spacing != 0)
            font.SpacingGlyph = spacing;
        return font;
    }

    public static StyleBoxFlat Box(Color bg, Color? border, int radius, float padH, float padV)
    {
        var box = new StyleBoxFlat
        {
            BgColor = bg,
            ContentMarginLeft = padH,
            ContentMarginRight = padH,
            ContentMarginTop = padV,
            ContentMarginBottom = padV,
            AntiAliasing = true,
        };
        box.SetCornerRadiusAll(radius);
        if (border is { } b)
        {
            box.BorderColor = b;
            box.SetBorderWidthAll(1);
        }
        return box;
    }

    /// <summary>Только мягкая тень под панелью: сама панель — стекло (шейдер glass).</summary>
    public static StyleBoxFlat Shadow(int radius)
    {
        var box = new StyleBoxFlat
        {
            DrawCenter = false,
            ShadowColor = new Color(0, 0, 0, 0.42f),
            ShadowSize = 22,
            ShadowOffset = new Vector2(0, 6),
            AntiAliasing = true,
        };
        box.SetCornerRadiusAll(radius);
        return box;
    }

    public static Label Text(string text, int size = 15, Font? font = null, Color? color = null)
    {
        var label = new Label { Text = text };
        label.AddThemeFontSizeOverride("font_size", size);
        if (font != null)
            label.AddThemeFontOverride("font", font);
        if (color is { } c)
            label.AddThemeColorOverride("font_color", c);
        return label;
    }

    public static Color Hex(string hex) => Color.FromHtml(hex);
}
