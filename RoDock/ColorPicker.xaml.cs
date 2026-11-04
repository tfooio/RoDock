using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace RoDock;

/// Colour square + hue bar + hex box. SetColor shows a colour silently; Changed fires only when the user picks one.
public partial class ColorPicker : UserControl
{
    const double W = 240, SvH = 150;
    double _h, _s = 1, _v = 1; // hue 0..360, saturation and value 0..1

    public event Action<Color>? Changed;

    public ColorPicker() { InitializeComponent(); Redraw(); }

    public Color Current => FromHsv(_h, _s, _v);

    public void SetColor(Color c)
    {
        var (h, s, v) = ToHsv(c);
        if (s > 0 && v > 0) _h = h; // keep the hue when the colour is grey or black (it has none)
        _s = s; _v = v;
        Redraw();
    }

    void Redraw()
    {
        SvHue.Fill = new SolidColorBrush(FromHsv(_h, 1, 1));
        Canvas.SetLeft(SvThumb, _s * W - 9); Canvas.SetTop(SvThumb, (1 - _v) * SvH - 9);
        Canvas.SetLeft(HueThumb, _h / 360 * W - 9);
        HueThumb.Fill = new SolidColorBrush(FromHsv(_h, 1, 1));
        var c = Current;
        SvThumb.Fill = new SolidColorBrush(c);
        if (!HexBox.IsKeyboardFocused) HexBox.Text = $"#{c.R:X2}{c.G:X2}{c.B:X2}";
    }

    void Pick() { Redraw(); Changed?.Invoke(Current); }

    void Sv_Down(object s, MouseButtonEventArgs e) { Sv.CaptureMouse(); Sv_Move(s, e); }
    void Sv_Move(object s, MouseEventArgs e)
    {
        if (!Sv.IsMouseCaptured) return;
        var p = e.GetPosition(Sv);
        _s = Math.Clamp(p.X / W, 0, 1); _v = 1 - Math.Clamp(p.Y / SvH, 0, 1);
        Pick();
    }

    void Hue_Down(object s, MouseButtonEventArgs e) { HueBar.CaptureMouse(); Hue_Move(s, e); }
    void Hue_Move(object s, MouseEventArgs e)
    {
        if (!HueBar.IsMouseCaptured) return;
        _h = Math.Clamp(e.GetPosition(HueBar).X / W, 0, 1) * 360;
        Pick();
    }

    void Release(object s, MouseButtonEventArgs e) => ((UIElement)s).ReleaseMouseCapture();

    void Hex_KeyDown(object s, KeyEventArgs e) { if (e.Key == Key.Enter) Hex_Commit(s, e); }

    void Hex_Commit(object s, RoutedEventArgs e)
    {
        var text = HexBox.Text.Trim();
        if (!text.StartsWith('#')) text = "#" + text;
        try { SetColor((Color)ColorConverter.ConvertFromString(text)); Changed?.Invoke(Current); }
        catch { Redraw(); } // not a colour: put the old one back
    }

    static (double H, double S, double V) ToHsv(Color c)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        var max = Math.Max(r, Math.Max(g, b)); var d = max - Math.Min(r, Math.Min(g, b));
        var h = d == 0 ? 0 : max == r ? 60 * (((g - b) / d + 6) % 6) : max == g ? 60 * ((b - r) / d + 2) : 60 * ((r - g) / d + 4);
        return (h, max == 0 ? 0 : d / max, max);
    }

    static Color FromHsv(double h, double s, double v)
    {
        double f(int n) { var k = (n + h / 60) % 6; return v - v * s * Math.Max(0, Math.Min(Math.Min(k, 4 - k), 1)); }
        return Color.FromRgb((byte)Math.Round(f(5) * 255), (byte)Math.Round(f(3) * 255), (byte)Math.Round(f(1) * 255));
    }
}
