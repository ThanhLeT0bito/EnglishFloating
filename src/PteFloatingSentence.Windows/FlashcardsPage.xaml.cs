using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using PteFloatingSentence.Core;
using PteFloatingSentence.Windows.Infrastructure;

using UserControl = System.Windows.Controls.UserControl;
using Button = System.Windows.Controls.Button;
using TextBox = System.Windows.Controls.TextBox;
using TextBlock = System.Windows.Controls.TextBlock;
using MessageBox = System.Windows.MessageBox;
using Color = System.Windows.Media.Color;
using Brushes = System.Windows.Media.Brushes;

namespace PteFloatingSentence.Windows;

public partial class FlashcardsPage : UserControl, IDisposable
{
    private AppSettings _settings = AppSettings.Default;
    private Action<AppSettings>? _onSettingsChanged;
    private IVocabularyExplainer? _explainer;
    private CancellationTokenSource? _aiCts;
    private string? _selectedDeckKey;
    private IReadOnlyList<FlashcardItem> _allCurrentDeckCards = [];
    private IReadOnlyList<FlashcardItem> _filteredCards = [];
    private enum CardFilterTab { All, Active, Done }
    private CardFilterTab _currentFilterTab = CardFilterTab.All;

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
        Unloaded += OnUnloaded;
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        Unloaded -= OnUnloaded;
        Dispose();
    }

    public void Dispose()
    {
        CancelAiGeneration();
    }

    private void CancelAiGeneration()
    {
        if (_aiCts is not null)
        {
            try
            {
                _aiCts.Cancel();
                _aiCts.Dispose();
            }
            catch (ObjectDisposedException) { }
            finally
            {
                _aiCts = null;
            }
        }
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
    public TextBox InputPhrase => CardPhraseInput;
    public TextBox InputPronunciation => CardPronunciationInput;
    public TextBox InputMeaning => CardMeaningInput;
    public TextBox InputExample => CardExampleInput;
    public TextBlock TextDialogError => DialogErrorText;
    public Button ButtonAiGenerate => AiGenerateButton;
    public Button ButtonAddCard => AddCardButton;

    public void OpenAddCardDialog()
    {
        AddCardButton_Click(this, new RoutedEventArgs());
    }

    public void LoadSettings(AppSettings settings, Action<AppSettings>? onSettingsChanged = null, IVocabularyExplainer? explainer = null)
    {
        _settings = settings;
        _onSettingsChanged = onSettingsChanged;
        if (explainer is not null)
        {
            _explainer = explainer;
        }

        RefreshDecksList();

        // If deck was already selected and still exists, keep it
        var summaries = FlashcardDeckProjection.GetDeckSummaries(_settings);
        var activeListDeckKey = FlashcardRules.StudyListDeckKey(_settings.ActiveListId);

        if (_selectedDeckKey is not null && summaries.Any(s => s.DeckKey == _selectedDeckKey))
        {
            SelectDeck(_selectedDeckKey);
        }
        else if (!string.IsNullOrEmpty(_settings.ActiveFlashcardDeckKey) && summaries.Any(s => s.DeckKey == _settings.ActiveFlashcardDeckKey))
        {
            SelectDeck(_settings.ActiveFlashcardDeckKey);
        }
        else if (summaries.Any(s => s.DeckKey == activeListDeckKey))
        {
            SelectDeck(activeListDeckKey);
        }
        else if (summaries.Count > 0)
        {
            SelectDeck(summaries[0].DeckKey);
        }
        else
        {
            _selectedDeckKey = null;
            UpdateDeckContentView();
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
            $"{s.ReadyCount} cards · {s.RememberedCount} remembered{(s.DoneCount > 0 ? $" · {s.DoneCount} done" : "")}"
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
            AddCardButton.Visibility = Visibility.Collapsed;
            DeleteDeckButton.Visibility = Visibility.Collapsed;
            _allCurrentDeckCards = [];
            _filteredCards = [];
            CardsListControl.ItemsSource = null;
            EmptyDeckNotice.Visibility = Visibility.Collapsed;
            FilterAllButton.Content = "All (0)";
            FilterActiveButton.Content = "Active (0)";
            FilterDoneButton.Content = "Done (0)";
            UpdateFilterButtonsVisual();
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
        SelectedDeckStatsText.Text = $"{currentSummary.ReadyCount} ready · {currentSummary.DoneCount} done · {currentSummary.RememberedCount} remembered · {currentSummary.UnavailableCount} unavailable";

        var isCustom = currentSummary.IsCustom;
        AddCardButton.Visibility = isCustom ? Visibility.Visible : Visibility.Collapsed;
        DeleteDeckButton.Visibility = isCustom ? Visibility.Visible : Visibility.Collapsed;

        _allCurrentDeckCards = FlashcardDeckProjection.GetDeckCards(_settings, _selectedDeckKey, includeUnavailable: true);
        StartStudyingButton.IsEnabled = _allCurrentDeckCards.Any(c => c.IsReady);

        var allCount = _allCurrentDeckCards.Count;
        var activeCount = _allCurrentDeckCards.Count(c => !c.IsMarkedDone);
        var doneCount = _allCurrentDeckCards.Count(c => c.IsMarkedDone);

        FilterAllButton.Content = $"All ({allCount})";
        FilterActiveButton.Content = $"Active ({activeCount})";
        FilterDoneButton.Content = $"Done ({doneCount})";
        UpdateFilterButtonsVisual();

        ApplyFilter();
    }

    private void FilterAllButton_Click(object sender, RoutedEventArgs e)
    {
        _currentFilterTab = CardFilterTab.All;
        UpdateFilterButtonsVisual();
        ApplyFilter();
    }

    private void FilterActiveButton_Click(object sender, RoutedEventArgs e)
    {
        _currentFilterTab = CardFilterTab.Active;
        UpdateFilterButtonsVisual();
        ApplyFilter();
    }

    private void FilterDoneButton_Click(object sender, RoutedEventArgs e)
    {
        _currentFilterTab = CardFilterTab.Done;
        UpdateFilterButtonsVisual();
        ApplyFilter();
    }

    private void UpdateFilterButtonsVisual()
    {
        SetFilterButtonActive(FilterAllButton, _currentFilterTab == CardFilterTab.All);
        SetFilterButtonActive(FilterActiveButton, _currentFilterTab == CardFilterTab.Active);
        SetFilterButtonActive(FilterDoneButton, _currentFilterTab == CardFilterTab.Done);
    }

    private static void SetFilterButtonActive(Button button, bool isActive)
    {
        button.Background = isActive
            ? new SolidColorBrush(Color.FromRgb(0x1D, 0x4E, 0xD8))
            : new SolidColorBrush(Color.FromRgb(0x1E, 0x29, 0x3B));
        button.BorderBrush = isActive
            ? new SolidColorBrush(Color.FromRgb(0x3B, 0x82, 0xF6))
            : new SolidColorBrush(Color.FromRgb(0x26, 0x34, 0x49));
        button.Foreground = isActive
            ? Brushes.White
            : new SolidColorBrush(Color.FromRgb(0x94, 0xA3, 0xB8));
    }

    private void CardDoneToggle_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.Tag is not string cardKey)
            return;

        var card = _allCurrentDeckCards.FirstOrDefault(c => c.CardKey == cardKey);
        if (card is null)
            return;

        var newStatus = !card.IsMarkedDone;
        _settings = FlashcardRules.SetMarkedDone(_settings, cardKey, newStatus, DateTimeOffset.UtcNow);
        _onSettingsChanged?.Invoke(_settings);
        RefreshDecksList();
        UpdateDeckContentView();
    }

    private void ApplyFilter()
    {
        var filter = PhraseSearchBox.Text?.Trim() ?? string.Empty;
        SearchPlaceholderText.Visibility = string.IsNullOrEmpty(filter) ? Visibility.Visible : Visibility.Collapsed;

        var baseList = _allCurrentDeckCards;
        if (_currentFilterTab == CardFilterTab.Active)
        {
            baseList = baseList.Where(c => !c.IsMarkedDone).ToList();
        }
        else if (_currentFilterTab == CardFilterTab.Done)
        {
            baseList = baseList.Where(c => c.IsMarkedDone).ToList();
        }

        if (string.IsNullOrEmpty(filter))
        {
            _filteredCards = baseList;
        }
        else
        {
            _filteredCards = baseList
                .Where(c => c.Phrase.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                            c.Meaning.Contains(filter, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        if (_allCurrentDeckCards.Count == 0)
        {
            CardsListControl.Visibility = Visibility.Collapsed;
            EmptyDeckNotice.Visibility = Visibility.Visible;

            var summaries = FlashcardDeckProjection.GetDeckSummaries(_settings);
            var summary = summaries.FirstOrDefault(s => s.DeckKey == _selectedDeckKey);
            if (summary is not null && summary.IsCustom)
            {
                EmptyDeckNoticeText.Text = "This custom deck has no cards yet. Click '+ Add Card' to create one.";
            }
            else
            {
                EmptyDeckNoticeText.Text = "No vocabulary found in this study list yet. Highlight words in the floating sentence to add them.";
            }
        }
        else if (_filteredCards.Count == 0)
        {
            CardsListControl.Visibility = Visibility.Collapsed;
            EmptyDeckNotice.Visibility = Visibility.Visible;
            EmptyDeckNoticeText.Text = !string.IsNullOrEmpty(filter)
                ? $"No cards match '{filter}'."
                : (_currentFilterTab == CardFilterTab.Done ? "No cards marked done yet." : "No active cards.");
        }
        else
        {
            CardsListControl.Visibility = Visibility.Visible;
            EmptyDeckNotice.Visibility = Visibility.Collapsed;

            var cardViewModels = _filteredCards.Select(c =>
            {
                var stateText = !c.IsReady
                    ? (!string.IsNullOrEmpty(c.UnavailableReason) && c.UnavailableReason.StartsWith("Pending") ? "Pending" : "Unavailable")
                    : c.State switch
                    {
                        FlashcardLearningState.Remembered => "Remembered",
                        FlashcardLearningState.Learning => "Learning",
                        _ => "New"
                    };

                var stateBrush = !c.IsReady
                    ? new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xF8, 0x71, 0x71))
                    : c.State switch
                    {
                        FlashcardLearningState.Remembered => (SolidColorBrush)FindResource("RememberedBrush"),
                        FlashcardLearningState.Learning => (SolidColorBrush)FindResource("HardBrush"),
                        _ => new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x38, 0xBD, 0xF8))
                    };

                var doneBorderBrush = c.IsMarkedDone
                    ? new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x10, 0xB9, 0x81))
                    : new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x33, 0x41, 0x55));
                var doneBackgroundBrush = c.IsMarkedDone
                    ? new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x26, 0x10, 0xB9, 0x81))
                    : new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x1E, 0x29, 0x3B));
                var doneForegroundBrush = c.IsMarkedDone
                    ? new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x34, 0xD3, 0x99))
                    : new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x94, 0xA3, 0xB8));
                var doneFontWeight = c.IsMarkedDone ? FontWeights.SemiBold : FontWeights.Normal;
                var doneText = "Done";
                var doneTooltip = c.IsMarkedDone
                    ? "Marked Done (Click to restore / study again)"
                    : "Click to mark as Done (exclude from floating loop)";

                return new CardRowViewModel(
                    c.Phrase,
                    c.PronunciationIpa,
                    c.Meaning,
                    c.Example,
                    c.SourceSentences.Count > 0 ? $"Sources ({c.SourceSentences.Count})" : string.Empty,
                    c.SourceSentences.Count > 0,
                    stateText,
                    stateBrush,
                    c.IsCustom,
                    c.CustomCardId,
                    c.CardKey,
                    c.IsMarkedDone,
                    doneBorderBrush,
                    doneBackgroundBrush,
                    doneForegroundBrush,
                    doneFontWeight,
                    doneText,
                    doneTooltip
                );
            }).ToList();

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

    #region In-Page Study Mode

    public void StartInPageStudy()
    {
        var readyCards = _allCurrentDeckCards.Where(c => c.IsReady).ToList();
        if (readyCards.Count == 0)
            return;

        _studyCards = readyCards;
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

    private void StudyMarkDoneButton_Click(object sender, RoutedEventArgs e) => MarkCurrentStudyCardDone();

    private void MarkCurrentStudyCardDone()
    {
        if (!_isStudying || _studyCards.Count == 0 || _studyIndex < 0 || _studyIndex >= _studyCards.Count)
            return;

        var card = _studyCards[_studyIndex];
        _settings = FlashcardRules.SetMarkedDone(_settings, card.CardKey, true, DateTimeOffset.UtcNow);
        _onSettingsChanged?.Invoke(_settings);

        var remaining = _studyCards.Where((c, i) => i != _studyIndex).ToList();
        if (remaining.Count == 0)
        {
            ExitInPageStudy();
            RefreshDecksList();
            UpdateDeckContentView();
            return;
        }

        _studyCards = remaining;
        if (_studyIndex >= _studyCards.Count)
            _studyIndex = 0;

        _studyIsShowingBack = false;
        UpdateStudyCardDisplay();
        RefreshDecksList();
    }

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
                case Key.D:
                    MarkCurrentStudyCardDone();
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
        CancelAiGeneration();
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
        CancelAiGeneration();
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

            CancelAiGeneration();
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
        CancelAiGeneration();
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
        CancelAiGeneration();
        DialogOverlay.Visibility = Visibility.Collapsed;
        _dialogMode = DialogMode.None;
    }

    public async Task<bool> AutoFillCardWithAiAsync(bool forceOverwrite = true, bool showError = true)
    {
        var phrase = CardPhraseInput.Text?.Trim();
        if (string.IsNullOrWhiteSpace(phrase))
        {
            if (showError)
            {
                DialogErrorText.Text = "Please enter a phrase first.";
                DialogErrorText.Visibility = Visibility.Visible;
            }
            return false;
        }

        if (_explainer is null)
        {
            if (showError)
            {
                DialogErrorText.Text = "Gemini API key is not configured or AI service is unavailable.";
                DialogErrorText.Visibility = Visibility.Visible;
            }
            return false;
        }

        CancelAiGeneration();
        _aiCts = new CancellationTokenSource();
        var token = _aiCts.Token;

        AiGenerateButton.IsEnabled = false;
        AiGenerateButton.Content = "✨ Generating...";
        DialogErrorText.Visibility = Visibility.Collapsed;

        try
        {
            var explanation = await _explainer.ExplainAsync(phrase, cancellationToken: token);
            if (token.IsCancellationRequested)
                return false;

            if (!string.IsNullOrWhiteSpace(explanation.CorrectedPhrase) &&
                !string.Equals(explanation.CorrectedPhrase, CardPhraseInput.Text?.Trim(), StringComparison.Ordinal))
            {
                CardPhraseInput.Text = explanation.CorrectedPhrase;
            }

            if (forceOverwrite || string.IsNullOrWhiteSpace(CardPronunciationInput.Text))
            {
                CardPronunciationInput.Text = explanation.PronunciationIpa ?? string.Empty;
            }

            if (forceOverwrite || string.IsNullOrWhiteSpace(CardMeaningInput.Text))
            {
                CardMeaningInput.Text = explanation.Meaning ?? string.Empty;
            }

            if (forceOverwrite || string.IsNullOrWhiteSpace(CardExampleInput.Text))
            {
                CardExampleInput.Text = explanation.Example ?? string.Empty;
            }

            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (Exception ex)
        {
            if (!token.IsCancellationRequested && showError)
            {
                DialogErrorText.Text = $"AI auto-fill failed: {ex.Message}";
                DialogErrorText.Visibility = Visibility.Visible;
            }
            return false;
        }
        finally
        {
            AiGenerateButton.IsEnabled = true;
            AiGenerateButton.Content = "✨ Auto-fill with AI";
        }
    }

    private async void AiGenerateButton_Click(object sender, RoutedEventArgs e)
    {
        await AutoFillCardWithAiAsync(forceOverwrite: true, showError: true);
    }

    private async void CardPhraseInput_LostFocus(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(CardPhraseInput.Text) &&
            string.IsNullOrWhiteSpace(CardMeaningInput.Text) &&
            string.IsNullOrWhiteSpace(CardExampleInput.Text))
        {
            await AutoFillCardWithAiAsync(forceOverwrite: false, showError: false);
        }
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
        Guid? CustomCardId,
        string CardKey,
        bool IsMarkedDone,
        SolidColorBrush DoneBorderBrush,
        SolidColorBrush DoneBackgroundBrush,
        SolidColorBrush DoneForegroundBrush,
        FontWeight DoneFontWeight,
        string DoneText,
        string DoneTooltip);
}
