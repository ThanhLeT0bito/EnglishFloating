using System.Windows;
using System.Windows.Controls;
using TextBox = System.Windows.Controls.TextBox;
using ListBoxItem = System.Windows.Controls.ListBoxItem;
using PteFloatingSentence.Core;
using PteFloatingSentence.Windows.Infrastructure;

namespace PteFloatingSentence.Windows;

public partial class SettingsWindow : Window
{
    private readonly StudyListDraft _draft;
    private readonly ProtectedApiKeyStore? _apiKeyStore;
    private readonly IVocabularyExplainer? _explainer;
    private bool _isRendering;
    private bool _apiKeyConfigured;
    private bool _apiKeyCleared;
    private readonly EventHandler<Guid> _setupPageStartPracticeHandler;

    public event EventHandler<Guid>? StartPracticeRequested;

    public SettingsPageId SelectedPage { get; private set; } = SettingsPageId.Setup;
    public FrameworkElement SectionFlashcards => FlashcardsSection;

    public void NavigateTo(SettingsPageId page)
    {
        SelectedPage = page;
        SyncNavigationSelection();
        UpdatePageHeader();
        RenderSelectedPage();
    }

    private void RenderSelectedPage()
    {
        if (SetupSection is not null)
            SetupSection.Visibility = SelectedPage == SettingsPageId.Setup ? Visibility.Visible : Visibility.Collapsed;
        if (DisplaySection is not null)
            DisplaySection.Visibility = SelectedPage == SettingsPageId.Display ? Visibility.Visible : Visibility.Collapsed;
        if (ReviewSection is not null)
            ReviewSection.Visibility = SelectedPage == SettingsPageId.Review ? Visibility.Visible : Visibility.Collapsed;
        if (ReviewPracticeSection is not null)
            ReviewPracticeSection.Visibility = SelectedPage == SettingsPageId.ReviewPractice ? Visibility.Visible : Visibility.Collapsed;
        if (GeminiSection is not null)
            GeminiSection.Visibility = SelectedPage == SettingsPageId.Gemini ? Visibility.Visible : Visibility.Collapsed;
        if (FlashcardsSection is not null)
            FlashcardsSection.Visibility = SelectedPage == SettingsPageId.Flashcards ? Visibility.Visible : Visibility.Collapsed;

        if (PageScrollViewer is not null)
        {
            PageScrollViewer.VerticalScrollBarVisibility = SelectedPage == SettingsPageId.Flashcards
                ? ScrollBarVisibility.Disabled
                : ScrollBarVisibility.Auto;
            PageScrollViewer.ScrollToTop();
        }
    }


    private void SyncNavigationSelection()
    {
        if (SettingsNavigation is null)
            return;

        foreach (ListBoxItem item in SettingsNavigation.Items)
        {
            if (item.Tag is SettingsPageId id && id == SelectedPage)
            {
                if (!item.IsSelected)
                {
                    _isRendering = true;
                    try
                    {
                        SettingsNavigation.SelectedItem = item;
                    }
                    finally
                    {
                        _isRendering = false;
                    }
                }
                break;
            }
        }
    }

    private void SettingsNavigation_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isRendering || SettingsNavigation.SelectedItem is not ListBoxItem item || item.Tag is not SettingsPageId pageId)
            return;

        NavigateTo(pageId);
    }

    private void UpdatePageHeader()
    {
        if (PageTitle is null || PageSubtitle is null)
            return;

        (PageTitle.Text, PageSubtitle.Text) = SelectedPage switch
        {
            SettingsPageId.Setup => ("Sentences", "Manage study lists and sentence practice order"),
            SettingsPageId.Flashcards => ("Flashcards", "Study sentence vocabulary and custom decks"),
            SettingsPageId.Display => ("Display", "Configure floating sentence and flashcard overlay preferences"),
            SettingsPageId.Review => ("Review", "Track vocabulary mastery and study progress"),
            SettingsPageId.ReviewPractice => ("Practice", "Practice sentences with masked hidden words"),
            SettingsPageId.Gemini => ("Gemini", "Configure Gemini API key for vocabulary explanations"),
            _ => ("Settings", string.Empty)
        };
    }

    public SettingsWindow(AppSettings initial, Action<AppSettings> save, ProtectedApiKeyStore? apiKeyStore = null, IVocabularyExplainer? explainer = null)
    {
        InitializeComponent();
        _draft = new StudyListDraft(initial, save);
        _apiKeyStore = apiKeyStore;
        _explainer = explainer;
        _apiKeyConfigured = initial.GeminiApiKeyConfigured;
        _setupPageStartPracticeHandler = OnSetupPageStartPracticeRequested;
        SetupPageControl.Initialize(_draft, ShowResult, () => RefreshUi());
        SetupPageControl.StartPracticeRequested += _setupPageStartPracticeHandler;
        DisplayPageControl.LoadPreferences(
            _draft.Settings.ShowSentenceOverlay,
            _draft.Settings.ShowVocabularyCards,
            _draft.Settings.ShowFloatingFlashcard,
            _draft.Settings.ActiveFlashcardDeckKey,
            FlashcardDeckProjection.GetDeckSummaries(_draft.Settings));
        DisplayPageControl.DisplayPreferencesChanged += OnDisplayPreferencesChanged;
        DisplayPageControl.FullDisplayPreferencesChanged += OnFullDisplayPreferencesChanged;
        GeminiPageControl.LoadState(_apiKeyConfigured);
        GeminiPageControl.ClearKeyRequested += OnClearKeyRequested;
        FlashcardsPageControl?.LoadSettings(_draft.Settings, OnFlashcardsSettingsChanged, _explainer);
        ReviewPracticePageControl?.Initialize(_draft.Settings, (listId, sentenceId, completed) =>
        {
            _draft.MarkSentenceCompleted(listId, sentenceId, completed);
            SetupPageControl.RefreshFromDraft(_draft);
            ReviewPageControl?.LoadData(new ReviewViewModel(_draft.Settings));
        });
        UpdateApiKeyStatus();
        SyncNavigationSelection();
        UpdatePageHeader();
        RenderSelectedPage();
        RefreshUi();
    }

    private void OnFlashcardsSettingsChanged(AppSettings newSettings)
    {
        _draft.UpdateFlashcardSettings(newSettings);
    }

    private void OnDisplayPreferencesChanged(bool showSentence, bool showVocab)
    {
        _draft.SetDisplayPreferences(showSentence, showVocab);
    }

    private void OnFullDisplayPreferencesChanged(bool showSentence, bool showVocab, bool showFloatingFlashcard, string? activeDeckKey)
    {
        _draft.SetDisplayPreferences(showSentence, showVocab, showFloatingFlashcard, activeDeckKey);
    }

    private void OnClearKeyRequested()
    {
        _apiKeyCleared = true;
        _apiKeyConfigured = false;
        UpdateApiKeyStatus();
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        if (SetupPageControl is not null)
        {
            SetupPageControl.StartPracticeRequested -= _setupPageStartPracticeHandler;
        }
        if (DisplayPageControl is not null)
        {
            DisplayPageControl.DisplayPreferencesChanged -= OnDisplayPreferencesChanged;
            DisplayPageControl.FullDisplayPreferencesChanged -= OnFullDisplayPreferencesChanged;
        }
        if (GeminiPageControl is not null)
        {
            GeminiPageControl.ClearKeyRequested -= OnClearKeyRequested;
        }
        ReviewPracticePageControl?.Dispose();
        FlashcardsPageControl?.Dispose();
    }

    private void NewListButton_Click(object sender, RoutedEventArgs e)
    {
        if (!TryUpdateSelectedList())
            return;

        _draft.CreateList();
        RefreshUi();
    }

    private void StudyListList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
    }

    private void MakeActiveButton_Click(object sender, RoutedEventArgs e)
    {
        if (!TryUpdateSelectedList())
            return;

        _draft.MakeSelectedListActive();
        RefreshUi();
    }

    private void DeleteListButton_Click(object sender, RoutedEventArgs e)
    {
        if (!TryUpdateSelectedList())
            return;

        if (System.Windows.MessageBox.Show(this, $"Delete '{_draft.SelectedList.Name}'?", "Delete study list", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        ShowResult(_draft.DeleteSelectedList());
        RefreshUi();
    }

    private void SentenceList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
    }

    private void AddSentenceButton_Click(object sender, RoutedEventArgs e)
    {
        if (!TryUpdateSelectedList())
            return;

        var sentenceInput = (TextBox)SetupPageControl.FindName("SentenceInput");
        var result = _draft.AddSentence(sentenceInput?.Text ?? string.Empty);
        ShowResult(result);
        if (result.IsValid)
            RefreshUi();
    }

    private void UpdateSentenceButton_Click(object sender, RoutedEventArgs e)
    {
        if (!TryUpdateSelectedList())
            return;

        var sentenceInput = (TextBox)SetupPageControl.FindName("SentenceInput");
        var result = _draft.UpdateSelectedSentence(sentenceInput?.Text ?? string.Empty);
        ShowResult(result);
        if (result.IsValid)
            RefreshUi();
    }

    private void DeleteSentenceButton_Click(object sender, RoutedEventArgs e)
    {
        if (!TryUpdateSelectedList())
            return;

        _draft.DeleteSelectedSentence();
        ValidationMessage.Text = string.Empty;
        RefreshUi();
    }

    private void ClearApiKeyButton_Click(object sender, RoutedEventArgs e)
    {
        GeminiPageControl.ClearApiKey();
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (!TryUpdateSelectedList())
            return;

        var newKey = GeminiPageControl.CurrentKey;
        if (!string.IsNullOrEmpty(newKey))
        {
            _apiKeyStore?.Save(newKey);
            _apiKeyConfigured = true;
        }
        else if (_apiKeyCleared)
        {
            _apiKeyStore?.Clear();
            _apiKeyConfigured = false;
        }

        _draft.SetApiKeyConfigured(_apiKeyConfigured);
        if (DisplayPageControl is not null)
            _draft.SetDisplayPreferences(
                DisplayPageControl.ShowSentenceOverlay,
                DisplayPageControl.ShowVocabularyCards,
                DisplayPageControl.ShowFloatingFlashcard,
                DisplayPageControl.ActiveFlashcardDeckKey);
        var result = _draft.Save();
        ShowResult(result);
        if (result.IsValid)
            Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        _draft.Cancel();
        Close();
    }

    private void UpdateApiKeyStatus()
    {
        GeminiPageControl?.LoadState(_apiKeyConfigured);
    }

    private bool TryUpdateSelectedList()
    {
        if (SetupPageControl is null)
            return true;

        return SetupPageControl.TryCommitListEditsInternal().IsValid;
    }

    private void RefreshUi()
    {
        _isRendering = true;
        try
        {
            SetupPageControl.RefreshFromDraft(_draft);
            DisplayPageControl?.LoadPreferences(
                _draft.Settings.ShowSentenceOverlay,
                _draft.Settings.ShowVocabularyCards,
                _draft.Settings.ShowFloatingFlashcard,
                _draft.Settings.ActiveFlashcardDeckKey,
                FlashcardDeckProjection.GetDeckSummaries(_draft.Settings));
            ReviewPageControl?.LoadData(new ReviewViewModel(_draft.Settings));
            GeminiPageControl?.LoadState(_apiKeyConfigured);
            FlashcardsPageControl?.LoadSettings(_draft.Settings, OnFlashcardsSettingsChanged, _explainer);
        }
        finally
        {
            _isRendering = false;
        }
    }

    public void UpdateSettingsFromApp(AppSettings newSettings)
    {
        _draft.UpdateSettingsFromApp(newSettings);
        RefreshUi();
    }

    public void OpenFlashcards(string? deckKey = null)
    {
        NavigateTo(SettingsPageId.Flashcards);
        if (!string.IsNullOrEmpty(deckKey))
        {
            FlashcardsPageControl?.SelectDeck(deckKey);
        }
    }

    private void TabButton_Checked(object sender, RoutedEventArgs e)
    {
        if (SetupSection is null || ReviewSection is null)
            return;

        var isReview = ReviewTabButton.IsChecked == true;
        SetupSection.Visibility = isReview ? Visibility.Collapsed : Visibility.Visible;
        ReviewSection.Visibility = isReview ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ShowResult(PteFloatingSentence.Core.ValidationResult result) => ValidationMessage.Text = result.IsValid ? string.Empty : result.Error;

    private void OnSetupPageStartPracticeRequested(object? sender, Guid listId)
    {
        var res = SetupPageControl.CommitListEdits?.Invoke() ?? new PteFloatingSentence.Core.ValidationResult(true, null);
        if (!res.IsValid)
        {
            ShowResult(res);
            return;
        }

        StartPracticeRequested?.Invoke(this, listId);
        Close();
    }
}
