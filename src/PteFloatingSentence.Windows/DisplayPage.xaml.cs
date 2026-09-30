using System.Windows;
using System.Windows.Controls;
using PteFloatingSentence.Core;
using UserControl = System.Windows.Controls.UserControl;
using CheckBox = System.Windows.Controls.CheckBox;
using ComboBox = System.Windows.Controls.ComboBox;

namespace PteFloatingSentence.Windows;

public partial class DisplayPage : UserControl
{
    private bool _isRendering;
    private string? _activeDeckKey;
    private IReadOnlyList<FlashcardDeckSummary> _availableDecks = [];

    public bool ShowSentenceOverlay => ShowSentenceOverlayInput.IsChecked == true;
    public bool ShowVocabularyCards => ShowVocabularyCardsInput.IsChecked == true;
    public bool ShowFloatingFlashcard => ShowFloatingFlashcardInput.IsChecked == true;
    public bool LaunchAtWindowsSignIn => LaunchAtWindowsSignInInput.IsChecked == true;
    public string? ActiveFlashcardDeckKey => _activeDeckKey;

    public event Action<bool, bool>? DisplayPreferencesChanged;
    public event Action<bool, bool, bool, string?>? FullDisplayPreferencesChanged;
    public event Action<bool>? LaunchAtWindowsSignInChanged;

    public DisplayPage()
    {
        InitializeComponent();
    }

    public void LoadPreferences(
        bool showSentenceOverlay,
        bool showVocabularyCards,
        bool showFloatingFlashcard = false,
        string? activeDeckKey = null,
        IReadOnlyList<FlashcardDeckSummary>? availableDecks = null,
        bool launchAtWindowsSignIn = true)
    {
        _isRendering = true;
        try
        {
            ShowSentenceOverlayInput.IsChecked = showSentenceOverlay;
            ShowVocabularyCardsInput.IsChecked = showVocabularyCards;
            ShowFloatingFlashcardInput.IsChecked = showFloatingFlashcard;
            LaunchAtWindowsSignInInput.IsChecked = launchAtWindowsSignIn;

            _availableDecks = availableDecks ?? [];
            _activeDeckKey = activeDeckKey;

            UpdateDeckComboBox();
            FloatingDeckSelectionPanel.IsEnabled = showFloatingFlashcard;
        }
        finally
        {
            _isRendering = false;
        }
    }

    private void UpdateDeckComboBox()
    {
        var items = _availableDecks.Select(d => new DisplayDeckItem(
            d.DeckKey,
            d.Name,
            d.IsCustom ? "(Custom)" : "(Study List)"
        )).ToList();

        FloatingDeckComboBox.ItemsSource = items;

        if (items.Count > 0)
        {
            var selectedIndex = items.FindIndex(i => i.DeckKey == _activeDeckKey);
            FloatingDeckComboBox.SelectedIndex = selectedIndex >= 0 ? selectedIndex : 0;
            if (selectedIndex < 0 && items.Count > 0)
            {
                _activeDeckKey = items[0].DeckKey;
            }
        }
        else
        {
            FloatingDeckComboBox.SelectedIndex = -1;
            _activeDeckKey = null;
        }
    }

    private void OnPreferenceChanged(object sender, RoutedEventArgs e)
    {
        if (_isRendering) return;

        FloatingDeckSelectionPanel.IsEnabled = ShowFloatingFlashcard;

        DisplayPreferencesChanged?.Invoke(ShowSentenceOverlay, ShowVocabularyCards);
        FullDisplayPreferencesChanged?.Invoke(ShowSentenceOverlay, ShowVocabularyCards, ShowFloatingFlashcard, _activeDeckKey);
        LaunchAtWindowsSignInChanged?.Invoke(LaunchAtWindowsSignIn);
    }

    private void FloatingDeckComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isRendering) return;

        if (FloatingDeckComboBox.SelectedItem is DisplayDeckItem item)
        {
            _activeDeckKey = item.DeckKey;
            DisplayPreferencesChanged?.Invoke(ShowSentenceOverlay, ShowVocabularyCards);
            FullDisplayPreferencesChanged?.Invoke(ShowSentenceOverlay, ShowVocabularyCards, ShowFloatingFlashcard, _activeDeckKey);
            LaunchAtWindowsSignInChanged?.Invoke(LaunchAtWindowsSignIn);
        }
    }

    private sealed record DisplayDeckItem(string DeckKey, string Name, string TypeBadge);
}
