using Godot;

namespace Politeconomy.Game.UI;

/// <summary>Цвета и шрифты интерфейса: тёмно-графитовые панели и один латунный акцент.</summary>
public static class UiTheme
{
    public static readonly Color Ocean = Color.FromHtml("#0e1217");
    public static readonly Color Panel = Color.FromHtml("#171b21f5");
    public static readonly Color PanelRaised = Color.FromHtml("#1e232a");
    public static readonly Color PanelBorder = Color.FromHtml("#2a3038");
    public static readonly Color ButtonBg = Color.FromHtml("#20262e");
    public static readonly Color ButtonHover = Color.FromHtml("#2a313b");
    public static readonly Color ButtonPressed = Color.FromHtml("#323b47");
    public static readonly Color TextPrimary = Color.FromHtml("#f0efec");
    public static readonly Color TextSecondary = Color.FromHtml("#c3c2b7");
    public static readonly Color TextMuted = Color.FromHtml("#898781");
    public static readonly Color Accent = Color.FromHtml("#d9a441");
    public static readonly Color Critical = Color.FromHtml("#d03b3b");
    public static readonly Color AccentLight = Color.FromHtml("#f0cd7a");
    public static readonly Color MapBorder = Color.FromHtml("#0b0e12");
    public static readonly Color MapCoast = Color.FromHtml("#0e1217cc");
    public static readonly Color MapRegionBorder = Color.FromHtml("#0e121766");
    public static readonly Color MapOutline = Color.FromHtml("#0e1217cc");

    public static Font Regular { get; private set; } = null!;
    public static Font Medium { get; private set; } = null!;
    public static Font SemiBold { get; private set; } = null!;
    public static Font SerifBold { get; private set; } = null!;

    public static Theme Build()
    {
        var plex = GD.Load<FontFile>("res://assets/fonts/IBMPlexSans-Variable.ttf");
        Regular = Weight(plex, 400);
        Medium = Weight(plex, 500);
        SemiBold = Weight(plex, 600);
        SerifBold = GD.Load<FontFile>("res://assets/fonts/PTSerif-Bold.ttf");

        var t = new Theme { DefaultFont = Regular, DefaultFontSize = 15 };

        t.SetStylebox("panel", "PanelContainer", Box(Panel, PanelBorder, 6, 14, 12));
        t.SetStylebox("panel", "Panel", Box(Panel, PanelBorder, 6, 0, 0));
        t.SetColor("font_color", "Label", TextPrimary);

        t.SetStylebox("normal", "Button", Box(ButtonBg, null, 4, 10, 5));
        t.SetStylebox("hover", "Button", Box(ButtonHover, null, 4, 10, 5));
        t.SetStylebox("pressed", "Button", Box(ButtonPressed, Accent, 4, 10, 5, bottomOnly: true));
        t.SetStylebox("hover_pressed", "Button", Box(ButtonPressed, Accent, 4, 10, 5, bottomOnly: true));
        t.SetStylebox("disabled", "Button", Box(ButtonBg, null, 4, 10, 5));
        t.SetStylebox("focus", "Button", new StyleBoxEmpty());
        t.SetColor("font_color", "Button", TextSecondary);
        t.SetColor("font_hover_color", "Button", TextPrimary);
        t.SetColor("font_pressed_color", "Button", Accent);
        t.SetColor("font_hover_pressed_color", "Button", Accent);
        t.SetColor("font_focus_color", "Button", TextSecondary);
        t.SetColor("font_disabled_color", "Button", TextMuted);
        t.SetFontSize("font_size", "Button", 14);

        t.SetStylebox("panel", "TooltipPanel", Box(PanelRaised, PanelBorder, 4, 12, 9));
        t.SetColor("font_color", "TooltipLabel", TextPrimary);
        t.SetFontSize("font_size", "TooltipLabel", 14);
        t.SetFont("font", "TooltipLabel", Regular);

        t.SetStylebox("separator", "HSeparator", new StyleBoxLine { Color = PanelBorder, Thickness = 1 });
        t.SetConstant("separation", "HSeparator", 14);
        t.SetStylebox("separator", "VSeparator", new StyleBoxLine { Color = PanelBorder, Thickness = 1, Vertical = true });
        t.SetConstant("separation", "VSeparator", 14);

        var grabber = Box(ButtonHover, null, 3, 0, 0);
        t.SetStylebox("scroll", "VScrollBar", new StyleBoxEmpty { ContentMarginLeft = 6 });
        t.SetStylebox("grabber", "VScrollBar", grabber);
        t.SetStylebox("grabber_highlight", "VScrollBar", Box(ButtonPressed, null, 3, 0, 0));
        t.SetStylebox("grabber_pressed", "VScrollBar", Box(ButtonPressed, null, 3, 0, 0));
        return t;
    }

    static FontVariation Weight(FontFile file, int weight)
    {
        var tag = TextServerManager.GetPrimaryInterface().NameToTag("wght");
        return new FontVariation
        {
            BaseFont = file,
            VariationOpentype = new Godot.Collections.Dictionary { { tag, weight } },
        };
    }

    public static StyleBoxFlat Box(Color bg, Color? border, int radius, float padH, float padV, bool bottomOnly = false)
    {
        var box = new StyleBoxFlat
        {
            BgColor = bg,
            ContentMarginLeft = padH,
            ContentMarginRight = padH,
            ContentMarginTop = padV,
            ContentMarginBottom = padV,
        };
        box.SetCornerRadiusAll(radius);
        if (border is { } b)
        {
            box.BorderColor = b;
            if (bottomOnly)
                box.BorderWidthBottom = 2;
            else
                box.SetBorderWidthAll(1);
        }
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
