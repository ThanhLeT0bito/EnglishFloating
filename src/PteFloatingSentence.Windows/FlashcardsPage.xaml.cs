using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using PteFloatingSentence.Core;

using UserControl = System.Windows.Controls.UserControl;
using Button = System.Windows.Controls.Button;
using MessageBox = System.Windows.MessageBox;

namespace PteFloatingSentence.Windows;

public partial class FlashcardsPage : UserControl
{
    private AppSettings _settings = AppSettings.Default;
    private Action<AppSettings>? _onSettingsChanged;
    private string? _selectedDeckKey;
    private IReadOnlyList<FlashcardItem> _allCurrentDeckCards = [];
    private IReadOnlyList<FlashcardItem> _filteredCards = [];

    // Study state
    private bool _isStudying;
    private int _studyIndex;
    private bool _studyIsShowingBack;
    private int _studyCycleCount;
    private IReadOnlyList<FlashcardItem> _studyCards = [];

    // Dialog state
    private enum DialogMode { None, NewDeck, AddCard, EditCard }
    private DialogMode _dialogMode = DialogMode.None;
    private Guid? _editingCardId;

    public FlashcardsPage()
    {
        InitializeComponent();
    }

    public string? SelectedDeckKey => _selectedDeckKey;
    public bool IsStudying => _isStudying;

    public string CurrentDeckName => SelectedDeckNameText.Text;
    public Button StartButton => StartStudyingButton;
    public FrameworkElement StudyView => InPageStudyView;
    public FrameworkElement ManagementView => DeckManagementView;
    public Button ButtonAgain => StudyAgainButton;
    public Button ButtonRemembered => StudyRememberedButton;
    public string CurrentStudyPhrase => StudyFrontPhraseText.Text;

    public void LoadSettings(AppSettings settings, Action<AppSettings>? onSettingsChanged = null)
    {
        _settings = settings;
        _onSettingsChanged = onSettingsChanged;

        RefreshDecksList();

        // If no deck selected yet or selected deck no longer exists, select first deck
        var summaries = FlashcardDeckProjection.GetDeckSummaries(_settings);
        if (!string.IsNullOrEmpty(_settings.ActiveFlashcardDeckKey) && summaries.Any(s => s.DeckKey == _settings.ActiveFlashcardDeckKey))
        {
            SelectDeck(_settings.ActiveFlashcardDeckKey);
        }
        else if (_selectedDeckKey is null || !summaries.Any(s => s.DeckKey == _selectedDeckKey))
        {
            if (summaries.Count > 0)
            {
                SelectDeck(summaries[0].DeckKey);
            }
            else
            {
                _selectedDeckKey = null;
                UpdateDeckContentView();
            }
        }
        else
        {
            SelectDeck(_selectedDeckKey);
        }
    }

    public void SelectDeck(string deckKey)
    {
        _selectedDeckKey = deckKey;

        // Select item in DecksListBox if not selected
        var summaries = FlashcardDeckProjection.GetDeckSummaries(_settings);
        var target = summaries.FirstOrDefault(s => s.DeckKey == deckKey);
        if (target is not null)
        {
            for (var i = 0; i < DecksListBox.Items.Count; i++)
            {
                if (DecksListBox.Items[i] is DeckSidebarItem item && item.DeckKey == deckKey)
                {
                    DecksListBox.SelectedIndex = i;
                    break;
                }
            }
        }

        UpdateDeckContentView();
    }

    private void RefreshDecksList()
    {
        var summaries = FlashcardDeckProjection.GetDeckSummaries(_settings);
        var items = summaries.Select(s => new DeckSidebarItem(
            s.DeckKey,
            s.Name,
            s.IsCustom ? "Custom" : "Study List",
            $"{s.ReadyCount} cards · {s.RememberedCount} remembered"
        )).ToList();

        DecksListBox.ItemsSource = items;
    }

    private void UpdateDeckContentView()
    {
        if (string.IsNullOrEmpty(_selectedDeckKey))
        {
            SelectedDeckNameText.Text = "Select a deck";
            SelectedDeckBadge.Visibility = Visibility.Collapsed;
            SelectedDeckStatsText.Text = string.Empty;
            StartStudyingButton.IsEnabled = false;
            FloatDeckToggleButton.IsEnabled = false;
            FloatDeckToggleButton.IsChecked = false;
            AddCardButton.Visibility = Visibility.Collapsed;
            DeleteDeckButton.Visibility = Visibility.Collapsed;
            _allCurrentDeckCards = [];
            _filteredCards = [];
            CardsListControl.ItemsSource = null;
            EmptyDeckNotice.Visibility = Visibility.Collapsed;
            return;
        }

        var summaries = FlashcardDeckProjection.GetDeckSummaries(_settings);
        var currentSummary = summaries.FirstOrDefault(s => s.DeckKey == _selectedDeckKey);

        if (currentSummary is null)
        {
            _selectedDeckKey = null;
            UpdateDeckContentView();
            return;
        }

        SelectedDeckNameText.Text = currentSummary.Name;
        SelectedDeckBadge.Visibility = Visibility.Visible;
        SelectedDeckBadgeText.Text = currentSummary.IsCustom ? "Custom Deck" : "Study List Deck";
        SelectedDeckStatsText.Text = $"{currentSummary.ReadyCount} ready · {currentSummary.UnavailableCount} unavailable · {currentSummary.RememberedCount} remembered";

        var isCustom = currentSummary.IsCustom;
        AddCardButton.Visibility = isCustom ? Visibility.Visible : Visibility.Collapsed;
        DeleteDeckButton.Visibility = isCustom ? Visibility.Visible : Visibility.Collapsed;

        var isFloatingActive = _settings.ShowFloatingFlashcard && _settings.ActiveFlashcardDeckKey == _selectedDeckKey;
        FloatDeckToggleButton.IsChecked = isFloatingActive;
        FloatDeckToggleText.Text = isFloatingActive ? "Floating Active" : "Float Flashcard";

        _allCurrentDeckCards = FlashcardDeckProjection.GetDeckCards(_settings, _selectedDeckKey);
        StartStudyingButton.IsEnabled = _allCurrentDeckCards.Count > 0;
        FloatDeckToggleButton.IsEnabled = _allCurrentDeckCards.Count > 0;

        ApplyFilter();
    }

    private void ApplyFilter()
    {
        var filter = PhraseSearchBox.Text?.Trim() ?? string.Empty;
        SearchPlaceholderText.Visibility = string.IsNullOrEmpty(filter) ? Visibility.Visible : Visibility.Collapsed;

        if (string.IsNullOrEmpty(filter))
        {
            _filteredCards = _allCurrentDeckCards;
        }
        else
        {
            _filteredCards = _allCurrentDeckCards
                .Where(c => c.Phrase.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                            c.Meaning.Contains(filter, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        if (_allCurrentDeckCards.Count == 0)
        {
            CardsScrollViewer.Visibility = Visibility.Collapsed;
            EmptyDeckNotice.Visibility = Visibility.Visible;

            var summaries = FlashcardDeckProjection.GetDeckSummaries(_settings);
            var summary = summaries.FirstOrDefault(s => s.DeckKey == _selectedDeckKey);
            if (summary is not null && summary.IsCustom)
            {
                EmptyDeckNoticeText.Text = "This custom deck has no cards yet. Click '+ Add Card' to create one.";
            }
            else
            {
                EmptyDeckNoticeText.Text = "No ready vocabulary found in this study list yet. Add vocabulary to sentences in the Setup page.";
            }
        }
        else if (_filteredCards.Count == 0)
        {
            CardsScrollViewer.Visibility = Visibility.Collapsed;
            EmptyDeckNotice.Visibility = Visibility.Visible;
            EmptyDeckNoticeText.Text = $"No cards match '{filter}'.";
        }
        else
        {
            CardsScrollViewer.Visibility = Visibility.Visible;
            EmptyDeckNotice.Visibility = Visibility.Collapsed;

            var cardViewModels = _filteredCards.Select(c => new CardRowViewModel(
                c.Phrase,
                c.PronunciationIpa,
                c.Meaning,
                c.Example,
                c.SourceSentences.Count > 0 ? $"Sources ({c.SourceSentences.Count})" : string.Empty,
                c.SourceSentences.Count > 0,
                c.State switch
                {
                    FlashcardLearningState.Remembered => "Remembered",
                    FlashcardLearningState.Learning => "Learning",
                    _ => "New"
                },
                c.State switch
                {
                    FlashcardLearningState.Remembered => (SolidColorBrush)FindResource("RememberedBrush"),
                    FlashcardLearningState.Learning => (SolidColorBrush)FindResource("HardBrush"),
                    _ => new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x38, 0xBD, 0xF8))
                },
                c.IsCustom,
                c.CustomCardId
            )).ToList();

            CardsListControl.ItemsSource = cardViewModels;
        }
    }

    private void DecksListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DecksListBox.SelectedItem is DeckSidebarItem item)
        {
            _selectedDeckKey = item.DeckKey;
            UpdateDeckContentView();
        }
    }

    private void PhraseSearchBox_TextChanged(object sender, TextChangedEventArgs e) => ApplyFilter();

    private void FloatDeckToggleButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_selectedDeckKey))
            return;

        var isChecked = FloatDeckToggleButton.IsChecked == true;
        _settings = _settings with
        {
            ShowFloatingFlashcard = isChecked,
            ActiveFlashcardDeckKey = isChecked ? _selectedDeckKey : _settings.ActiveFlashcardDeckKey
        };

        FloatDeckToggleText.Text = isChecked ? "Floating Active" : "Float Flashcard";
        _onSettingsChanged?.Invoke(_settings);
    }

    #region In-Page Study Mode

    public void StartInPageStudy()
    {
        if (_allCurrentDeckCards.Count == 0)
            return;

        _studyCards = _allCurrentDeckCards;
        _studyIndex = 0;
        _studyIsShowingBack = false;
        _studyCycleCount = 0;
        _isStudying = true;

        DeckManagementView.Visibility = Visibility.Collapsed;
        InPageStudyView.Visibility = Visibility.Visible;

        var summaries = FlashcardDeckProjection.GetDeckSummaries(_settings);
        var summary = summaries.FirstOrDefault(s => s.DeckKey == _selectedDeckKey);
        StudyDeckTitleText.Text = summary?.Name ?? "Study Deck";

        UpdateStudyCardDisplay();
        Focus();
    }

    public void ExitInPageStudy()
    {
        _isStudying = false;
        InPageStudyView.Visibility = Visibility.Collapsed;
        DeckManagementView.Visibility = Visibility.Visible;
        UpdateDeckContentView();
    }

    private void UpdateStudyCardDisplay()
    {
        if (_studyCards.Count == 0 || _studyIndex < 0 || _studyIndex >= _studyCards.Count)
        {
            ExitInPageStudy();
            return;
        }

        var card = _studyCards[_studyIndex];
        StudyFrontPhraseText.Text = card.Phrase;
        StudyFrontPronunciationText.Text = card.PronunciationIpa ?? string.Empty;
        StudyFrontPronunciationText.Visibility = string.IsNullOrWhiteSpace(card.PronunciationIpa) ? Visibility.Collapsed : Visibility.Visible;

        StudyBackPhraseText.Text = card.Phrase;
        StudyBackPronunciationText.Text = card.PronunciationIpa ?? string.Empty;
        StudyBackPronunciationText.Visibility = string.IsNullOrWhiteSpace(card.PronunciationIpa) ? Visibility.Collapsed : Visibility.Visible;
        StudyBackMeaningText.Text = card.Meaning;
        StudyBackExampleText.Text = card.Example;

        if (card.SourceSentences.Count > 0)
        {
            StudySourceList.ItemsSource = card.SourceSentences;
            StudySourceExpander.Visibility = Visibility.Visible;
        }
        else
        {
            StudySourceExpander.Visibility = Visibility.Collapsed;
        }

        var cycleInfo = _studyCycleCount > 0 ? $" • Cycle {_studyCycleCount}" : string.Empty;
        StudyProgressText.Text = $"Card {_studyIndex + 1} / {_studyCards.Count}{cycleInfo}";

        StudyStateBadgeText.Text = card.State switch
        {
            FlashcardLearningState.Remembered => "Remembered",
            FlashcardLearningState.Learning => "Learning",
            _ => "New"
        };
        StudyStateBadgeText.Foreground = card.State switch
        {
            FlashcardLearningState.Remembered => (SolidColorBrush)FindResource("RememberedBrush"),
            FlashcardLearningState.Learning => (SolidColorBrush)FindResource("HardBrush"),
            _ => new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x38, 0xBD, 0xF8))
        };

        UpdateStudyFaceVisibility();
    }

    private void UpdateStudyFaceVisibility()
    {
        StudyFrontPanel.Visibility = _studyIsShowingBack ? Visibility.Collapsed : Visibility.Visible;
        StudyBackPanel.Visibility = _studyIsShowingBack ? Visibility.Visible : Visibility.Collapsed;

        var canRate = _studyIsShowingBack && _studyCards.Count > 0;
        StudyAgainButton.IsEnabled = canRate;
        StudyHardButton.IsEnabled = canRate;
        StudyRememberedButton.IsEnabled = canRate;
    }

    public void FlipStudyCard()
    {
        if (!_isStudying || _studyCards.Count == 0)
            return;

        _studyIsShowingBack = !_studyIsShowingBack;
        UpdateStudyFaceVisibility();
    }

    public void StudyNext()
    {
        if (!_isStudying || _studyCards.Count == 0)
            return;

        _studyIndex = (_studyIndex + 1) % _studyCards.Count;
        _studyIsShowingBack = false;
        UpdateStudyCardDisplay();
    }

    public void StudyPrev()
    {
        if (!_isStudying || _studyCards.Count == 0)
            return;

        _studyIndex = (_studyIndex - 1 + _studyCards.Count) % _studyCards.Count;
        _studyIsShowingBack = false;
        UpdateStudyCardDisplay();
    }

    public void SubmitStudyRating(FlashcardRating rating)
    {
        if (!_isStudying || !_studyIsShowingBack || _studyCards.Count == 0)
            return;

        var card = _studyCards[_studyIndex];
        var wasLast = _studyIndex == _studyCards.Count - 1;

        // Apply rating to settings
        _settings = FlashcardRules.ApplyRating(_settings, card.CardKey, rating, DateTimeOffset.UtcNow);
        _onSettingsChanged?.Invoke(_settings);

        // Update current cards with new progress
        if (_selectedDeckKey is not null)
        {
            _allCurrentDeckCards = FlashcardDeckProjection.GetDeckCards(_settings, _selectedDeckKey);
            _studyCards = _allCurrentDeckCards;
        }

        if (wasLast)
        {
            _studyCycleCount++;
            _studyIndex = 0;
        }
        else
        {
            _studyIndex++;
        }

        _studyIsShowingBack = false;
        UpdateStudyCardDisplay();
    }

    private void StartStudyingButton_Click(object sender, RoutedEventArgs e) => StartInPageStudy();

    private void ExitStudyButton_Click(object sender, RoutedEventArgs e) => ExitInPageStudy();

    private void StudyCardSurface_MouseLeftButtonUp(object sender, MouseButtonEventArgs e) => FlipStudyCard();

    private void StudyPrevButton_Click(object sender, RoutedEventArgs e) => StudyPrev();

    private void StudyNextButton_Click(object sender, RoutedEventArgs e) => StudyNext();

    private void StudyAgainButton_Click(object sender, RoutedEventArgs e) => SubmitStudyRating(FlashcardRating.Again);

    private void StudyHardButton_Click(object sender, RoutedEventArgs e) => SubmitStudyRating(FlashcardRating.Hard);

    private void StudyRememberedButton_Click(object sender, RoutedEventArgs e) => SubmitStudyRating(FlashcardRating.Remembered);

    private void UserControl_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (_isStudying)
        {
            switch (e.Key)
            {
                case Key.Space:
                    FlipStudyCard();
                    e.Handled = true;
                    break;
                case Key.Left:
                    StudyPrev();
                    e.Handled = true;
                    break;
                case Key.Right:
                    StudyNext();
                    e.Handled = true;
                    break;
                case Key.D1:
                case Key.NumPad1:
                    if (_studyIsShowingBack)
                    {
                        SubmitStudyRating(FlashcardRating.Again);
                        e.Handled = true;
                    }
                    break;
                case Key.D2:
                case Key.NumPad2:
                    if (_studyIsShowingBack)
                    {
                        SubmitStudyRating(FlashcardRating.Hard);
                        e.Handled = true;
                    }
                    break;
                case Key.D3:
                case Key.NumPad3:
                    if (_studyIsShowingBack)
                    {
                        SubmitStudyRating(FlashcardRating.Remembered);
                        e.Handled = true;
                    }
                    break;
                case Key.Escape:
                    ExitInPageStudy();
                    e.Handled = true;
                    break;
            }
        }
    }

    #endregion

    #region Dialogs & CRUD

    private void NewDeckButton_Click(object sender, RoutedEventArgs e)
    {
        _dialogMode = DialogMode.NewDeck;
        DialogTitleText.Text = "Create Custom Deck";
        DeckNameInput.Text = string.Empty;
        DeckFormPanel.Visibility = Visibility.Visible;
        CardFormPanel.Visibility = Visibility.Collapsed;
        DialogErrorText.Visibility = Visibility.Collapsed;
        DialogOverlay.Visibility = Visibility.Visible;
        DeckNameInput.Focus();
    }

    private void DeleteDeckButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_selectedDeckKey) || !_selectedDeckKey.StartsWith("custom:", StringComparison.OrdinalIgnoreCase))
            return;

        var idStr = _selectedDeckKey["custom:".Length..];
        if (!Guid.TryParse(idStr, out var deckId))
            return;

        var result = MessageBox.Show(
            "Are you sure you want to delete this custom deck and all its flashcards?",
            "Delete Deck",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result == MessageBoxResult.Yes)
        {
            _settings = FlashcardRules.DeleteCustomDeck(_settings, deckId);
            _onSettingsChanged?.Invoke(_settings);
            RefreshDecksList();
            var summaries = FlashcardDeckProjection.GetDeckSummaries(_settings);
            _selectedDeckKey = summaries.FirstOrDefault()?.DeckKey;
            UpdateDeckContentView();
        }
    }

    private void AddCardButton_Click(object sender, RoutedEventArgs e)
    {
        _dialogMode = DialogMode.AddCard;
        _editingCardId = null;
        DialogTitleText.Text = "Add Custom Card";
        CardPhraseInput.Text = string.Empty;
        CardPronunciationInput.Text = string.Empty;
        CardMeaningInput.Text = string.Empty;
        CardExampleInput.Text = string.Empty;
        DeckFormPanel.Visibility = Visibility.Collapsed;
        CardFormPanel.Visibility = Visibility.Visible;
        DialogErrorText.Visibility = Visibility.Collapsed;
        DialogOverlay.Visibility = Visibility.Visible;
        CardPhraseInput.Focus();
    }

    private void EditCardButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is Guid cardId && _selectedDeckKey?.StartsWith("custom:", StringComparison.OrdinalIgnoreCase) == true)
        {
            var idStr = _selectedDeckKey["custom:".Length..];
            if (!Guid.TryParse(idStr, out var deckId))
                return;

            var deck = _settings.CustomFlashcardDecks.FirstOrDefault(d => d.Id == deckId);
            var card = deck?.Cards.FirstOrDefault(c => c.Id == cardId);
            if (card is null)
                return;

            _dialogMode = DialogMode.EditCard;
            _editingCardId = cardId;
            DialogTitleText.Text = "Edit Custom Card";
            CardPhraseInput.Text = card.Phrase;
            CardPronunciationInput.Text = card.PronunciationIpa ?? string.Empty;
            CardMeaningInput.Text = card.Meaning;
            CardExampleInput.Text = card.Example;
            DeckFormPanel.Visibility = Visibility.Collapsed;
            CardFormPanel.Visibility = Visibility.Visible;
            DialogErrorText.Visibility = Visibility.Collapsed;
            DialogOverlay.Visibility = Visibility.Visible;
            CardPhraseInput.Focus();
        }
    }

    private void DeleteCardButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is Guid cardId && _selectedDeckKey?.StartsWith("custom:", StringComparison.OrdinalIgnoreCase) == true)
        {
            var idStr = _selectedDeckKey["custom:".Length..];
            if (!Guid.TryParse(idStr, out var deckId))
                return;

            var result = MessageBox.Show(
                "Are you sure you want to delete this card?",
                "Delete Card",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                _settings = FlashcardRules.DeleteCustomCard(_settings, deckId, cardId);
                _onSettingsChanged?.Invoke(_settings);
                RefreshDecksList();
                UpdateDeckContentView();
            }
        }
    }

    private void DialogSaveButton_Click(object sender, RoutedEventArgs e)
    {
        DialogErrorText.Visibility = Visibility.Collapsed;

        if (_dialogMode == DialogMode.NewDeck)
        {
            var name = DeckNameInput.Text;
            var validation = FlashcardRules.ValidateCustomDeckName(name);
            if (!validation.IsValid)
            {
                DialogErrorText.Text = validation.Error;
                DialogErrorText.Visibility = Visibility.Visible;
                return;
            }

            _settings = FlashcardRules.CreateCustomDeck(_settings, name);
            _onSettingsChanged?.Invoke(_settings);
            RefreshDecksList();

            var newDeck = _settings.CustomFlashcardDecks.LastOrDefault();
            if (newDeck is not null)
            {
                SelectDeck(FlashcardRules.CustomDeckKey(newDeck.Id));
            }

            DialogOverlay.Visibility = Visibility.Collapsed;
        }
        else if (_dialogMode == DialogMode.AddCard || _dialogMode == DialogMode.EditCard)
        {
            if (string.IsNullOrEmpty(_selectedDeckKey) || !_selectedDeckKey.StartsWith("custom:", StringComparison.OrdinalIgnoreCase))
                return;

            var idStr = _selectedDeckKey["custom:".Length..];
            if (!Guid.TryParse(idStr, out var deckId))
                return;

            var deck = _settings.CustomFlashcardDecks.FirstOrDefault(d => d.Id == deckId);
            var validation = FlashcardRules.ValidateCustomCard(
                CardPhraseInput.Text,
                CardPronunciationInput.Text,
                CardMeaningInput.Text,
                CardExampleInput.Text,
                deck,
                _editingCardId);

            if (!validation.IsValid)
            {
                DialogErrorText.Text = validation.Error;
                DialogErrorText.Visibility = Visibility.Visible;
                return;
            }

            if (_dialogMode == DialogMode.AddCard)
            {
                _settings = FlashcardRules.AddCustomCard(
                    _settings,
                    deckId,
                    CardPhraseInput.Text,
                    CardPronunciationInput.Text,
                    CardMeaningInput.Text,
                    CardExampleInput.Text);
            }
            else if (_dialogMode == DialogMode.EditCard && _editingCardId.HasValue)
            {
                _settings = FlashcardRules.UpdateCustomCard(
                    _settings,
                    deckId,
                    _editingCardId.Value,
                    CardPhraseInput.Text,
                    CardPronunciationInput.Text,
                    CardMeaningInput.Text,
                    CardExampleInput.Text);
            }

            _onSettingsChanged?.Invoke(_settings);
            RefreshDecksList();
            UpdateDeckContentView();
            DialogOverlay.Visibility = Visibility.Collapsed;
        }
    }

    private void DialogCancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogOverlay.Visibility = Visibility.Collapsed;
        _dialogMode = DialogMode.None;
    }

    #endregion

    private sealed record DeckSidebarItem(
        string DeckKey,
        string Name,
        string TypeBadge,
        string StatsText);

    private sealed record CardRowViewModel(
        string Phrase,
        string? PronunciationIpa,
        string Meaning,
        string Example,
        string SourceInfo,
        bool HasSource,
        string StateText,
        SolidColorBrush StateBrush,
        bool IsCustom,
        Guid? CustomCardId);
}
