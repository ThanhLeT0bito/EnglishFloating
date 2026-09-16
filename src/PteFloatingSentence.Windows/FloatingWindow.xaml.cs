using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using PteFloatingSentence.Core;

namespace PteFloatingSentence.Windows;

public partial class FloatingWindow : Window
{
    private bool _hasMultipleSentences;

    public FloatingWindow()
    {
        InitializeComponent();
    }

    public event EventHandler? SettingsRequested;
    public event EventHandler? ExitRequested;
    public event EventHandler? PreviousRequested;
    public event EventHandler? NextRequested;
    public event EventHandler<(double Left, double Top)>? PositionChanged;

    public void ApplySettings(AppSettings settings)
    {
        var defaults = AppSettings.Default;
        var activeList = StudyListRules.ActiveList(settings);
        var sentenceCount = activeList.Sentences.Count;
        _hasMultipleSentences = sentenceCount > 1;
        PreviousButton.IsEnabled = _hasMultipleSentences;
        NextButton.IsEnabled = _hasMultipleSentences;
        UpdateNavigationVisibility(isPointerOver: IsMouseOver);

        SentenceText.Text = sentenceCount == 0
            ? "Add a sentence in Settings."
            : activeList.Sentences[activeList.CurrentSentenceIndex].Text;
        ProgressText.Text = $"{activeList.Name} · {(sentenceCount == 0 ? 0 : activeList.CurrentSentenceIndex + 1)} / {sentenceCount}";
        SentenceText.FontSize = IsValidFontSize(settings.FontSize) ? settings.FontSize : defaults.FontSize;
        SentenceText.Foreground = ToBrush(settings.TextColor, defaults.TextColor);
        SentenceBackground.Background = ToBlackBackground(settings.BackgroundOpacity, defaults.BackgroundOpacity);
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (IsWithinButtonTree(e.OriginalSource, PreviousButton) || IsWithinButtonTree(e.OriginalSource, NextButton))
            return;

        DragMove();
        PositionChanged?.Invoke(this, (Left, Top));
    }

    private void Window_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e) => UpdateNavigationVisibility(isPointerOver: true);

    private void Window_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e) => UpdateNavigationVisibility(isPointerOver: false);

    private void PreviousButton_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        PreviousRequested?.Invoke(this, EventArgs.Empty);
    }

    private void NextButton_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        NextRequested?.Invoke(this, EventArgs.Empty);
    }

    private void SettingsMenuItem_Click(object sender, RoutedEventArgs e) => SettingsRequested?.Invoke(this, EventArgs.Empty);

    private void ExitMenuItem_Click(object sender, RoutedEventArgs e) => ExitRequested?.Invoke(this, EventArgs.Empty);

    private void UpdateNavigationVisibility(bool isPointerOver)
    {
        var visibility = _hasMultipleSentences && isPointerOver ? Visibility.Visible : Visibility.Collapsed;
        PreviousButton.Visibility = visibility;
        NextButton.Visibility = visibility;
    }

    private static bool IsWithinButtonTree(object originalSource, System.Windows.Controls.Button button)
    {
        for (var current = originalSource as FrameworkElement; current is not null; current = current.Parent as FrameworkElement)
        {
            if (ReferenceEquals(current, button))
                return true;
        }

        return false;
    }

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
