using System.Windows;
using System.Windows.Input;

namespace PteFloatingSentence.Windows;

public partial class OverlayLauncherWindow : Window
{
    public event EventHandler? SettingsRequested;
    public event EventHandler? RestoreRequested;

    public OverlayLauncherWindow()
    {
        InitializeComponent();
    }

    private void LauncherActionButton_Click(object sender, RoutedEventArgs e)
    {
        SettingsRequested?.Invoke(this, EventArgs.Empty);
    }

    private void SettingsMenuItem_Click(object sender, RoutedEventArgs e)
    {
        SettingsRequested?.Invoke(this, EventArgs.Empty);
    }

    private void RestoreMenuItem_Click(object sender, RoutedEventArgs e)
    {
        RestoreRequested?.Invoke(this, EventArgs.Empty);
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source && IsWithinElement(source, LauncherActionButton))
            return;

        DragMove();
    }

    private void Window_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key is Key.Enter or Key.Space)
        {
            e.Handled = true;
            SettingsRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    public void RaiseSettingsRequested() => SettingsRequested?.Invoke(this, EventArgs.Empty);

    public void RaiseRestoreRequested() => RestoreRequested?.Invoke(this, EventArgs.Empty);

    private static bool IsWithinElement(DependencyObject source, FrameworkElement element)
    {
        for (var cur = source; cur is not null; cur = System.Windows.Media.VisualTreeHelper.GetParent(cur))
        {
            if (ReferenceEquals(cur, element))
                return true;
        }

        return false;
    }
}
