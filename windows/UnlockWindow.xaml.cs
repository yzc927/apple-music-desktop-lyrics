using System.Windows;
using System.Windows.Media.Animation;

namespace AppleMusicDesktopLyrics;

public partial class UnlockWindow : Window
{
    private readonly Action _unlock;
    private int _animationVersion;

    public UnlockWindow(Action unlock)
    {
        InitializeComponent();
        _unlock = unlock;
    }

    private void Unlock_Click(object sender, RoutedEventArgs e) => _unlock();

    public void ShowWithFade()
    {
        _animationVersion++;
        BeginAnimation(OpacityProperty, null);
        if (!IsVisible)
        {
            Opacity = 0;
            Show();
        }
        BeginAnimation(OpacityProperty, new DoubleAnimation(Opacity, 1,
            TimeSpan.FromMilliseconds(220))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        });
    }

    public void HideWithFade()
    {
        if (!IsVisible) return;
        var version = ++_animationVersion;
        var animation = new DoubleAnimation(Opacity, 0, TimeSpan.FromMilliseconds(220))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
        };
        animation.Completed += (_, _) =>
        {
            if (version == _animationVersion) Hide();
        };
        BeginAnimation(OpacityProperty, animation);
    }

    public void HideImmediately()
    {
        _animationVersion++;
        BeginAnimation(OpacityProperty, null);
        Opacity = 0;
        Hide();
    }
}
