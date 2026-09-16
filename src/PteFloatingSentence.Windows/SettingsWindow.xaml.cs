using System.Text.RegularExpressions;
using System.Windows;
using PteFloatingSentence.Core;

namespace PteFloatingSentence.Windows;

public partial class SettingsWindow : Window
{
    private static readonly Regex ColorPattern = new("^#[0-9A-Fa-f]{8}$", RegexOptions.Compiled);
    private readonly AppSettings _initial;
    private readonly Action<AppSettings> _save;

    public SettingsWindow(AppSettings initial, Action<AppSettings> save)
    {
        _initial = initial;
        _save = save;
        InitializeComponent();
        SentenceInput.Text = initial.Sentence;
        FontSizeInput.Text = initial.FontSize.ToString();
        TextColorInput.Text = initial.TextColor;
        OpacityInput.Text = initial.BackgroundOpacity.ToString();
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        var validation = SentenceValidator.Validate(SentenceInput.Text);
        if (!validation.IsValid)
        {
            ValidationMessage.Text = validation.Error;
            return;
        }

        if (!double.TryParse(FontSizeInput.Text, out var size) || !SettingsNumericValidator.IsValidFontSize(size))
        {
            ValidationMessage.Text = "Font size must be from 12 to 96.";
            return;
        }

        if (!double.TryParse(OpacityInput.Text, out var opacity) || !SettingsNumericValidator.IsValidOpacity(opacity))
        {
            ValidationMessage.Text = "Opacity must be from 0 to 1.";
            return;
        }

        if (!ColorPattern.IsMatch(TextColorInput.Text))
        {
            ValidationMessage.Text = "Use an ARGB color such as #FFFFFFFF.";
            return;
        }

        _save(_initial with
        {
            Sentence = SentenceInput.Text.Trim(),
            FontSize = size,
            TextColor = TextColorInput.Text,
            BackgroundOpacity = opacity
        });
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) => Close();
}
