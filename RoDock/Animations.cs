using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shell;

namespace RoDock;

/// The one switch for all UI motion, saved in the settings and shown as "Motion" in the right rail.
/// Every animation here is a short one-shot on a render transform or opacity, so none of them touches layout, and each does nothing when motion is off.
/// While on, every animation is capped at 30 fps (see Program.Main): measured at about half the CPU of 60 fps.
///
/// What moves, and the rule behind it:
///   Hook()            buttons and toggles shrink to 0.97 while pressed; switch knobs slide ease-in-out and stretch 12%.
///   Enter()           entrances: ease-out, 200 ms, fade plus an 8 px rise (panels, views, list rows staggered 30 ms) or a 0.95 scale from the trigger (popovers).
///   Pulse / Fade      attached properties for the Dot and run-glyph styles: one pulse when a status turns good, a quick fade when the glyph swaps.
/// Nothing bounces. Exits are not animated: the visibility flips are bound, so they stay instant.
public class Animations : INotifyPropertyChanged
{
    public static Animations State { get; } = new();

    bool _on = true;
    public bool On
    {
        get => _on;
        set { if (_on == value) return; _on = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(On))); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    static readonly IEasingFunction Out = Ease(EasingMode.EaseOut), InOut = Ease(EasingMode.EaseInOut);
    static IEasingFunction Ease(EasingMode m) { var e = new CubicEase { EasingMode = m }; e.Freeze(); return e; }

    /// Keyframes through (value, ms) points; the property falls back to its own value afterwards.
    static DoubleAnimationUsingKeyFrames Keys(IEasingFunction? ease, params (double v, int ms)[] k)
    {
        var a = new DoubleAnimationUsingKeyFrames { FillBehavior = FillBehavior.Stop };
        foreach (var (v, ms) in k) a.KeyFrames.Add(new EasingDoubleKeyFrame(v, TimeSpan.FromMilliseconds(ms), ease ?? Out));
        return a;
    }

    /// `from` to `to` over `ms`, after `delay` (the property already shows `from` while it waits).
    static void Run(IAnimatable t, DependencyProperty p, double from, double to, int delay = 0, int ms = 200, IEasingFunction? ease = null)
        => t.BeginAnimation(p, delay > 0 ? Keys(ease, (from, 0), (from, delay), (to, delay + ms)) : Keys(ease, (from, 0), (to, ms)));

    /// Moves a number property to `to` over `ms` milliseconds (ease-out), or jumps there when motion is off.
    /// hold: false lets the property fall back to its own value afterwards, so `to` must equal that value.
    public static void Animate(IAnimatable target, DependencyProperty property, double to, int ms, bool hold = true)
    {
        if (!State.On) { target.BeginAnimation(property, null); ((DependencyObject)target).SetValue(property, to); return; }
        target.BeginAnimation(property, new DoubleAnimation(to, TimeSpan.FromMilliseconds(ms)) { EasingFunction = Out, FillBehavior = hold ? FillBehavior.HoldEnd : FillBehavior.Stop });
    }

    /// The scale and offset every animation here uses, created on first use. Origin is the centre unless Enter moves it.
    static (ScaleTransform, TranslateTransform) Xf(UIElement el)
    {
        if (el.RenderTransform is TransformGroup { Children: [ScaleTransform s, TranslateTransform t] }) return (s, t);
        ScaleTransform sc = new(); TranslateTransform tr = new();
        el.RenderTransform = new TransformGroup { Children = { sc, tr } };
        el.RenderTransformOrigin = new Point(.5, .5);
        return (sc, tr);
    }

    /// Entrance: fades in over 200 ms, rising `rise` px and/or growing from `scale` around `origin` (popovers start at 0.95, at their trigger). `delay` staggers list rows.
    public static void Enter(UIElement el, int delay = 0, double rise = 8, double scale = 1, Point? origin = null)
    {
        if (!State.On) return;
        var (s, t) = Xf(el);
        if (origin is { } o) el.RenderTransformOrigin = o;
        Run(el, UIElement.OpacityProperty, 0, 1, delay);
        if (rise != 0) Run(t, TranslateTransform.YProperty, rise, 0, delay);
        if (scale != 1) { Run(s, ScaleTransform.ScaleXProperty, scale, 1, delay); Run(s, ScaleTransform.ScaleYProperty, scale, 1, delay); }
    }

    /// Call once before any window exists: class handlers have to be in place before the first Button is created.
    public static void Hook()
    {
        foreach (var type in new[] { typeof(Button), typeof(ToggleButton) })
        {
            EventManager.RegisterClassHandler(type, UIElement.PreviewMouseLeftButtonDownEvent, new MouseButtonEventHandler((s, _) => Press((FrameworkElement)s, true)));
            EventManager.RegisterClassHandler(type, UIElement.PreviewMouseLeftButtonUpEvent, new MouseButtonEventHandler((s, _) => Press((FrameworkElement)s, false)));
            EventManager.RegisterClassHandler(type, Mouse.LostMouseCaptureEvent, new MouseEventHandler((s, _) => Press((FrameworkElement)s, false)));
        }
        foreach (var e in new[] { ToggleButton.CheckedEvent, ToggleButton.UncheckedEvent })
            EventManager.RegisterClassHandler(typeof(CheckBox), e, new RoutedEventHandler(Thumb));
    }

    /// Press shrinks to 0.97 in 90 ms and holds while held; release returns in 140 ms. The title bar buttons keep their plain hover.
    static void Press(FrameworkElement b, bool down)
    {
        if (down ? !State.On || WindowChrome.GetIsHitTestVisibleInChrome(b) : b.RenderTransform is not TransformGroup) return;
        var (s, _) = Xf(b);
        Animate(s, ScaleTransform.ScaleXProperty, down ? .97 : 1, down ? 90 : 140, down);
        Animate(s, ScaleTransform.ScaleYProperty, down ? .97 : 1, down ? 90 : 140, down);
    }

    /// Switch knob: the Setters in SwitchBox flip it to its new side at once, this starts it from the old side (18 px away), ease-in-out, stretched 12% on the way.
    static void Thumb(object sender, RoutedEventArgs e)
    {
        var c = (CheckBox)sender;
        if (!State.On || !c.IsLoaded || c.Template.FindName("slide", c) is not TranslateTransform slide) return;
        Run(slide, TranslateTransform.XProperty, c.IsChecked == true ? -18 : 18, 0, 0, 200, InOut);
        ((ScaleTransform)c.Template.FindName("stretch", c)).BeginAnimation(ScaleTransform.ScaleXProperty, Keys(null, (1, 0), (1.12, 100), (1, 200)));
    }

    // Attached properties: bind them to a value in a Style and the animation plays when that value changes after the element is on screen.
    static DependencyProperty Cue(string name, Action<UIElement, object?> play) => DependencyProperty.RegisterAttached(name, typeof(object), typeof(Animations),
        new PropertyMetadata(null, (d, e) => { if (State.On && d is FrameworkElement { IsLoaded: true } el) play(el, e.NewValue); }));

    /// A status dot grows to 1.5 and settles when its status becomes Good (the one success confirmation).
    public static readonly DependencyProperty PulseProperty = Cue("Pulse", (el, v) =>
    {
        if (v is not Kind.Good) return;
        var (s, _) = Xf(el);
        var a = Keys(null, (1, 0), (1.5, 160), (1, 400));
        s.BeginAnimation(ScaleTransform.ScaleXProperty, a); s.BeginAnimation(ScaleTransform.ScaleYProperty, a);
    });
    public static object? GetPulse(DependencyObject d) => d.GetValue(PulseProperty);
    public static void SetPulse(DependencyObject d, object? v) => d.SetValue(PulseProperty, v);

    /// An icon fades in over 160 ms whenever the bound value changes (the run glyph swapping play and stop).
    public static readonly DependencyProperty FadeProperty = Cue("Fade", (el, _) => Run(el, UIElement.OpacityProperty, 0, 1, 0, 160));
    public static object? GetFade(DependencyObject d) => d.GetValue(FadeProperty);
    public static void SetFade(DependencyObject d, object? v) => d.SetValue(FadeProperty, v);
}
