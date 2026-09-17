using System.Windows;
using System.Windows.Controls;
using UserControl = System.Windows.Controls.UserControl;
using CheckBox = System.Windows.Controls.CheckBox;

namespace PteFloatingSentence.Windows;

public partial class DisplayPage : UserControl
{
    private bool _isRendering;

    public bool ShowSentenceOverlay => ShowSentenceOverlayInput.IsChecked == true;
    public bool ShowVocabularyCards => ShowVocabularyCardsInput.IsChecked == true;

    public event Action<bool, bool>? DisplayPreferencesChanged;

    public DisplayPage()
    {
        InitializeComponent();
    }

    public void LoadPreferences(bool showSentenceOverlay, bool showVocabularyCards)
    {
        _isRendering = true;
        try
        {
            ShowSentenceOverlayInput.IsChecked = showSentenceOverlay;
            ShowVocabularyCardsInput.IsChecked = showVocabularyCards;
        }
        finally
        {
            _isRendering = false;
        }
    }

    private void OnPreferenceChanged(object sender, RoutedEventArgs e)
    {
        if (_isRendering) return;
        DisplayPreferencesChanged?.Invoke(ShowSentenceOverlay, ShowVocabularyCards);
    }
}
