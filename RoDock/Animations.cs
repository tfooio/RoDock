using System.ComponentModel;
using System.Windows;
using System.Windows.Media.Animation;

namespace RoDock;

/// The one switch for all UI motion, saved in the settings and shown as "Motion" in the right rail.
/// In code, animate through Animate(): it just sets the value when motion is off.
/// In XAML, put the animation in a trigger on this switch so it only runs while on and stops when turned off:
///   &lt;DataTrigger Binding="{Binding On, Source={x:Static local:Animations.State}}" Value="True"&gt; EnterActions / ExitActions &lt;/DataTrigger&gt;
/// While on, every animation is capped at 30 fps (see Program.Main): measured at about half the CPU of 60 fps.
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

    /// Moves a number property to `to` over `ms` milliseconds, or jumps there when motion is off.
    public static void Animate(UIElement element, DependencyProperty property, double to, int ms)
    {
        if (!State.On) { element.BeginAnimation(property, null); element.SetValue(property, to); return; }
        element.BeginAnimation(property, new DoubleAnimation(to, TimeSpan.FromMilliseconds(ms)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
    }
}
