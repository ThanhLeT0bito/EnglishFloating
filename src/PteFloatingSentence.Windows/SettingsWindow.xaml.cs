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
    private bool _isRendering;
    private bool _apiKeyConfigured;
    private bool _apiKeyCleared;

    public SettingsPageId SelectedPage { get; private set; } = SettingsPageId.Setup;

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
            SettingsPageId.Setup => ("Setup", "Manage study lists and sentence practice order"),
            SettingsPageId.Display => ("Display", "Configure floating sentence and vocabulary overlay preferences"),
            SettingsPageId.Review => ("Review", "Track vocabulary mastery and study progress"),
            SettingsPageId.ReviewPractice => ("Practice", "Practice sentences with masked hidden words"),
            SettingsPageId.Gemini => ("Gemini", "Configure Gemini API key for vocabulary explanations"),
            _ => ("Settings", string.Empty)
        };
    }

    public SettingsWindow(AppSettings initial, Action<AppSettings> save, ProtectedApiKeyStore? apiKeyStore = null)
    {
        InitializeComponent();
        _draft = new StudyListDraft(initial, save);
        _apiKeyStore = apiKeyStore;
        _apiKeyConfigured = initial.GeminiApiKeyConfigured;
        SetupPageControl.Initialize(_draft, ShowResult, () => RefreshUi());
        DisplayPageControl.LoadPreferences(_draft.Settings.ShowSentenceOverlay, _draft.Settings.ShowVocabularyCards);
        DisplayPageControl.DisplayPreferencesChanged += OnDisplayPreferencesChanged;
        GeminiPageControl.LoadState(_apiKeyConfigured);
        GeminiPageControl.ClearKeyRequested += OnClearKeyRequested;
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

    private void OnDisplayPreferencesChanged(bool showSentence, bool showVocab)
    {
        _draft.SetDisplayPreferences(showSentence, showVocab);
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
        if (DisplayPageControl is not null)
        {
            DisplayPageControl.DisplayPreferencesChanged -= OnDisplayPreferencesChanged;
        }
        if (GeminiPageControl is not null)
        {
            GeminiPageControl.ClearKeyRequested -= OnClearKeyRequested;
        }
        ReviewPracticePageControl?.Dispose();
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
            _draft.SetDisplayPreferences(DisplayPageControl.ShowSentenceOverlay, DisplayPageControl.ShowVocabularyCards);
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
            DisplayPageControl?.LoadPreferences(_draft.Settings.ShowSentenceOverlay, _draft.Settings.ShowVocabularyCards);
            ReviewPageControl?.LoadData(new ReviewViewModel(_draft.Settings));
            GeminiPageControl?.LoadState(_apiKeyConfigured);
        }
        finally
        {
            _isRendering = false;
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
}
