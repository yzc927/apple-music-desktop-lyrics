using System.Windows;

namespace AppleMusicDesktopLyrics;

public partial class UnlockWindow : Window
{
    private readonly Action _unlock;

    public UnlockWindow(Action unlock)
    {
        InitializeComponent();
        _unlock = unlock;
    }

    private void Unlock_Click(object sender, RoutedEventArgs e) => _unlock();
}
