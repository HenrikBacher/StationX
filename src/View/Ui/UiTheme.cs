using Godot;

namespace StationX.View.Ui;

public static class Fonts
{
    public static Font Body = null!;
    public static Font BodyBold = null!;
    public static Font Heading = null!;
    public static Font Typewriter = null!;

    public static void Load()
    {
        var franklin = GD.Load<FontFile>("res://assets/fonts/LibreFranklin.ttf");
        var baskerville = GD.Load<FontFile>("res://assets/fonts/LibreBaskerville.ttf");
        Body = Weight(franklin, 450);
        BodyBold = Weight(franklin, 650);
        Heading = Weight(baskerville, 700);
        Typewriter = GD.Load<FontFile>("res://assets/fonts/SpecialElite-Regular.ttf");
    }

    static Font Weight(FontFile f, int weight)
    {
        var v = new FontVariation { BaseFont = f };
        var tag = TextServerManager.GetPrimaryInterface().NameToTag("wght");
        v.VariationOpentype = new Godot.Collections.Dictionary { { tag, weight } };
        return v;
    }
}

/// <summary>Wartime office look: dark olive panels, cream text, brass accents.</summary>
public static class UiTheme
{
    public static readonly Color Panel = new("2e2b22");
    public static readonly Color PanelLight = new("3b382c");
    public static readonly Color PanelEdge = new("57513d");
    public static readonly Color Text = new("ece3cc");
    public static readonly Color TextDim = new("b3a98f");
    public static readonly Color Accent = new("c9a14a");
    public static readonly Color AccentDark = new("8f7130");
    public static readonly Color PaperBg = new("efe6d2");
    public static readonly Color PaperInk = new("2b2620");
    public static readonly Color Stamp = new("a8322a");

    public static Theme Build()
    {
        var t = new Theme { DefaultFont = Fonts.Body, DefaultFontSize = 15 };

        t.SetColor("font_color", "Label", Text);
        t.SetColor("font_color", "Button", Text);
        t.SetColor("font_hover_color", "Button", Colors.White);
        t.SetColor("font_pressed_color", "Button", PaperInk);
        t.SetColor("font_disabled_color", "Button", TextDim with { A = 0.55f });
        t.SetColor("font_focus_color", "Button", Text);
        t.SetColor("default_color", "RichTextLabel", Text);
        t.SetFont("bold_font", "RichTextLabel", Fonts.BodyBold);
        t.SetFont("normal_font", "RichTextLabel", Fonts.Body);

        t.SetStylebox("panel", "PanelContainer", Box(Panel, PanelEdge, 6, 12));
        t.SetStylebox("panel", "Panel", Box(Panel, PanelEdge, 6, 0));

        t.SetStylebox("normal", "Button", Box(PanelLight, PanelEdge, 4, 8, 5));
        t.SetStylebox("hover", "Button", Box(PanelLight.Lightened(0.1f), Accent, 4, 8, 5));
        t.SetStylebox("pressed", "Button", Box(Accent, Accent, 4, 8, 5));
        t.SetStylebox("disabled", "Button", Box(Panel, PanelEdge with { A = 0.5f }, 4, 8, 5));
        t.SetStylebox("focus", "Button", Box(new Color(0, 0, 0, 0), Accent, 4, 8, 5));

        t.SetStylebox("background", "ProgressBar", Box(new Color("1f1d17"), PanelEdge, 3, 0));
        t.SetStylebox("fill", "ProgressBar", Box(Accent, Accent, 3, 0));
        t.SetColor("font_color", "ProgressBar", Text);

        t.SetStylebox("separator", "HSeparator", new StyleBoxLine { Color = PanelEdge, Thickness = 1 });
        t.SetConstant("separation", "HSeparator", 10);

        t.SetStylebox("panel", "TooltipPanel", Box(PaperBg, PaperInk, 3, 8));
        t.SetColor("font_color", "TooltipLabel", PaperInk);
        return t;
    }

    public static StyleBoxFlat Box(Color bg, Color border, int radius, int margin, int vmargin = -1)
    {
        var s = new StyleBoxFlat
        {
            BgColor = bg,
            BorderColor = border,
            CornerRadiusTopLeft = radius, CornerRadiusTopRight = radius,
            CornerRadiusBottomLeft = radius, CornerRadiusBottomRight = radius,
            ContentMarginLeft = margin, ContentMarginRight = margin,
            ContentMarginTop = vmargin < 0 ? margin : vmargin,
            ContentMarginBottom = vmargin < 0 ? margin : vmargin,
        };
        s.SetBorderWidthAll(1);
        return s;
    }

    public static StyleBoxFlat Paper()
    {
        var s = Box(PaperBg, new Color("b9ab88"), 2, 36, 30);
        s.ShadowColor = new Color(0, 0, 0, 0.45f);
        s.ShadowSize = 18;
        s.ShadowOffset = new Vector2(4, 8);
        return s;
    }

    public static Label Label(string text, int size = 15, Color? color = null, Font? font = null, bool wrap = true)
    {
        var l = new Label { Text = text, AutowrapMode = wrap ? TextServer.AutowrapMode.WordSmart : TextServer.AutowrapMode.Off };
        l.AddThemeFontSizeOverride("font_size", size);
        if (color is Color c) l.AddThemeColorOverride("font_color", c);
        if (font != null) l.AddThemeFontOverride("font", font);
        return l;
    }

    public static Label Heading(string text, int size = 20, Color? color = null, bool wrap = false) =>
        Label(text, size, color ?? Text, Fonts.Heading, wrap);

    public static Label Caps(string text, int size = 11, Color? color = null)
    {
        var l = Label(text.ToUpperInvariant(), size, color ?? TextDim, Fonts.BodyBold, wrap: false);
        l.AddThemeConstantOverride("outline_size", 0);
        return l;
    }

    public static Button Button(string text, System.Action onPressed, bool enabled = true, string? tooltip = null)
    {
        var b = new Button { Text = text, Disabled = !enabled, TooltipText = tooltip ?? "", FocusMode = Control.FocusModeEnum.None };
        b.Pressed += () => StationX.View.Sound.Play("click", 0.5f);
        b.Pressed += onPressed;
        return b;
    }
}
