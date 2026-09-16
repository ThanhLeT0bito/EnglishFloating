using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using PteFloatingSentence.Core;

namespace PteFloatingSentence.Windows;

public partial class FloatingWindow : Window
{
    public FloatingWindow()
    {
        InitializeComponent();
    }

    public event EventHandler? SettingsRequested;
    public event EventHandler? ExitRequested;
    public event EventHandler<(double Left, double Top)>? PositionChanged;

    public void ApplySettings(AppSettings settings)
    {
        var defaults = AppSettings.Default;
        SentenceText.Text = settings.Sentence ?? defaults.Sentence;
        SentenceText.FontSize = IsValidFontSize(settings.FontSize) ? settings.FontSize : defaults.FontSize;
        SentenceText.Foreground = ToBrush(settings.TextColor, defaults.TextColor);
        SentenceBackground.Background = ToBlackBackground(settings.BackgroundOpacity, defaults.BackgroundOpacity);
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        DragMove();
        PositionChanged?.Invoke(this, (Left, Top));
    }

    private void SettingsMenuItem_Click(object sender, RoutedEventArgs e) => SettingsRequested?.Invoke(this, EventArgs.Empty);

    private void ExitMenuItem_Click(object sender, RoutedEventArgs e) => ExitRequested?.Invoke(this, EventArgs.Empty);

    private static bool IsValidFontSize(double value) => !double.IsNaN(value) && !double.IsInfinity(value) && value is >= 12 and <= 96;

    private static System.Windows.Media.Brush ToBrush(string? textColor, string fallbackColor)
    {
        try
        {
            var color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(textColor ?? fallbackColor)!;
            return new SolidColorBrush(color);
        }
        catch (Exception)
        {
            return new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(fallbackColor)!);
        }
    }

    private static System.Windows.Media.Brush ToBlackBackground(double opacity, double fallbackOpacity)
    {
        var selectedOpacity = !double.IsNaN(opacity) && !double.IsInfinity(opacity) && opacity is >= 0 and <= 1
            ? opacity
            : fallbackOpacity;
        return new SolidColorBrush(System.Windows.Media.Color.FromArgb((byte)Math.Round(selectedOpacity * byte.MaxValue), 0, 0, 0));
    }
}
