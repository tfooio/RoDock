using System.Windows;
using System.Windows.Media;

namespace RoDock;

/// A colour scheme: five base colours as #RRGGBB. Everything else (borders, muted text, overlays) is derived from them in Themes.Apply.
public record Theme(string Name, string Bg, string Card, string Deep, string Accent, string Text)
{
    public static readonly string[] Slots = ["Bg", "Card", "Deep", "Accent", "Text"];

    public string Get(string slot) => slot switch { "Bg" => Bg, "Card" => Card, "Deep" => Deep, "Accent" => Accent, _ => Text };

    public Theme With(string slot, string hex) => slot switch
    {
        "Bg" => this with { Bg = hex },
        "Card" => this with { Card = hex },
        "Deep" => this with { Deep = hex },
        "Accent" => this with { Accent = hex },
        _ => this with { Text = hex },
    };
}

public static class Themes
{
    public static readonly Theme[] Presets =
    [
        new("Ink", "#0E131C", "#141B26", "#0A0E15", "#7DB2F2", "#E8EEF7"),
        new("Dusty blue", "#3A5878", "#2B4666", "#1E3552", "#8CC4FF", "#F4F8FF"),
        new("Midnight", "#14161F", "#1D2030", "#0D0F16", "#7C8CFF", "#EEF0FA"),
        new("Forest", "#2F4A3F", "#243A31", "#182A22", "#7EE0A6", "#F1F8F3"),
        new("Rose", "#5A3547", "#46293A", "#321C2A", "#FF9CC2", "#FFF3F8"),
        new("Sunset", "#4A3A33", "#3A2D27", "#271E1A", "#FFB05C", "#FFF6EE"),
    ];

    /// Presets (or their saved edits) followed by the user's own themes.
    public static List<Theme> Merge(IEnumerable<Theme>? saved)
    {
        var saved2 = (saved ?? []).ToList();
        return Presets.Select(p => saved2.FirstOrDefault(s => s.Name == p.Name) ?? p)
                      .Concat(saved2.Where(s => Presets.All(p => p.Name != s.Name))).ToList();
    }

    public static Color Parse(string hex)
    {
        try { return (Color)ColorConverter.ConvertFromString(hex); }
        catch { return Colors.Magenta; } // a hand-edited bad value should be obvious, not a crash
    }

    static Color Mix(Color a, Color b, double t) => Color.FromRgb(
        (byte)Math.Round(a.R + (b.R - a.R) * t), (byte)Math.Round(a.G + (b.G - a.G) * t), (byte)Math.Round(a.B + (b.B - a.B) * t));

    static Color Alpha(Color c, double a) => Color.FromArgb((byte)Math.Round(255 * a), c.R, c.G, c.B);

    public static double Luma(Color c) => (0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B) / 255;

    /// Replaces the palette brushes of the running app; every DynamicResource follows.
    public static void Apply(Theme t)
    {
        Color bg = Parse(t.Bg), card = Parse(t.Card), deep = Parse(t.Deep), accent = Parse(t.Accent), text = Parse(t.Text);
        var muted = Mix(text, bg, 0.22);
        // Ink is what sits on light text-coloured buttons and on the dark overlays: dark for light text, white for dark text.
        var ink = Luma(text) > 0.5 ? Mix(deep, Colors.Black, 0.45) : Colors.White;

        var r = Application.Current.Resources;
        void Set(string key, Color c) { var b = new SolidColorBrush(c); b.Freeze(); r[key] = b; }
        Set("Bg", bg); Set("Card", card); Set("Deep", deep); Set("Accent", accent); Set("Text", text);
        Set("Muted", muted);
        Set("Ink", ink);
        Set("Scrim", Alpha(ink, 0.82));
        Set("Raised", Mix(card, text, 0.07));
        Set("Neutral", Mix(card, text, 0.22));
        Set("CardHi", Mix(card, text, 0.12));
        Set("BarDim", Mix(card, accent, 0.7));
        Set("Idle", Mix(muted, card, 0.35));
        Set("TextSoft", Alpha(text, 0.16)); Set("TextEdge", Alpha(text, 0.35)); Set("TextFaint", Alpha(text, 0.10)); Set("Line", Alpha(text, 0.09));
        Set("AccentSoft", Alpha(accent, 0.18)); Set("AccentFill", Alpha(accent, 0.20));
        Set("DeepDim", Alpha(deep, 0.65)); Set("DeepOverlay", Alpha(deep, 0.95));
    }
}
