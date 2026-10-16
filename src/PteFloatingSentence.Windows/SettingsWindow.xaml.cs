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
    private readonly ProtectedApiKeyStore? _groqKeyStore;
    private readonly ProtectedApiKeyStore? _claudeKeyStore;
    private bool _groqKeyConfigured;
    private bool _claudeKeyConfigured;
    private bool _groqKeyCleared;
    private bool _claudeKeyCleared;
    private readonly IVocabularyExplainer? _explainer;
    private bool _isRendering;
    private bool _apiKeyConfigured;
    private bool _apiKeyCleared;
    private readonly EventHandler<Guid> _setupPageStartPracticeHandler;
    private readonly WritingHistoryBook _writingHistory;
    private RoadmapSession? _roadmapSession;

    public event EventHandler<Guid>? StartPracticeRequested;

    public SettingsPageId SelectedPage { get; private set; } = SettingsPageId.Setup;
    public FrameworkElement SectionFlashcards => FlashcardsSection;

    /// <summary>Today's roadmap session while it runs, otherwise null.</summary>
    public RoadmapSession? RoadmapSession => _roadmapSession;

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
        if (WritingSection is not null)
            WritingSection.Visibility = SelectedPage == SettingsPageId.Writing ? Visibility.Visible : Visibility.Collapsed;
        if (GrammarSection is not null)
            GrammarSection.Visibility = SelectedPage == SettingsPageId.Grammar ? Visibility.Visible : Visibility.Collapsed;
        if (RoadmapSection is not null)
            RoadmapSection.Visibility = SelectedPage == SettingsPageId.Roadmap ? Visibility.Visible : Visibility.Collapsed;

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
            SettingsPageId.Gemini => ("AI", "Choose the AI provider and manage API keys"),
            SettingsPageId.Writing => ("Writing", "Translate Vietnamese sentences into English and get AI feedback"),
            SettingsPageId.Grammar => ("Grammar", "Understand the grammar you get wrong, then practice it"),
            SettingsPageId.Roadmap => ("Roadmap", "Your writing and grammar path, one stage at a time, with a short session each day"),
            _ => ("Settings", string.Empty)
        };
    }

    public SettingsWindow(AppSettings initial, Action<AppSettings> save, ProtectedApiKeyStore? apiKeyStore = null, IVocabularyExplainer? explainer = null, ISentencePhraser? sentencePhraser = null, IWritingCoach? writingCoach = null,
        ProtectedApiKeyStore? groqKeyStore = null, ProtectedApiKeyStore? claudeKeyStore = null, WritingHistoryBook? writingHistory = null,
        GrammarLessonLibrary? grammarLibrary = null)
    {
        InitializeComponent();
        _draft = new StudyListDraft(initial, save);
        _apiKeyStore = apiKeyStore;
        _explainer = explainer;
        _apiKeyConfigured = initial.GeminiApiKeyConfigured;
        _groqKeyStore = groqKeyStore;
        _claudeKeyStore = claudeKeyStore;
        _groqKeyConfigured = initial.GroqApiKeyConfigured;
        _claudeKeyConfigured = initial.ClaudeApiKeyConfigured;
        _setupPageStartPracticeHandler = OnSetupPageStartPracticeRequested;
        SetupPageControl.Initialize(_draft, ShowResult, () => RefreshUi());
        SetupPageControl.SentencePhraser = sentencePhraser;
        SetupPageControl.StartPracticeRequested += _setupPageStartPracticeHandler;
        DisplayPageControl.LoadSettings(_draft.Settings);
        DisplayPageControl.DisplayPreferencesChanged += OnDisplayPreferencesChanged;
        DisplayPageControl.FullDisplayPreferencesChanged += OnFullDisplayPreferencesChanged;
        DisplayPageControl.LaunchAtWindowsSignInChanged += OnLaunchAtWindowsSignInChanged;
        DisplayPageControl.TtsPreferencesChanged += OnTtsPreferencesChanged;
        DisplayPageControl.PracticeAudioDelayChanged += OnPracticeAudioDelayChanged;
        GeminiPageControl.LoadState(_apiKeyConfigured);
        GeminiPageControl.ClearKeyRequested += OnClearKeyRequested;
        GeminiPageControl.ProviderChanged += OnAiProviderChanged;
        GeminiPageControl.ClearProviderKeyRequested += OnClearProviderKeyRequested;
        FlashcardsPageControl?.LoadSettings(_draft.Settings, OnFlashcardsSettingsChanged, _explainer);
        ReviewPracticePageControl.PracticeModeChanged += OnPracticeModeChanged;
        ReviewPracticePageControl?.Initialize(_draft.Settings, (listId, sentenceId, completed) =>
        {
            _draft.MarkSentenceCompleted(listId, sentenceId, completed);
            SetupPageControl.RefreshFromDraft(_draft);
            ReviewPageControl?.LoadData(new ReviewViewModel(_draft.Settings));
        });
        // Both pages read the same notebook so a check on the Writing page updates the Grammar page.
        var history = writingHistory ?? WritingHistoryBook.InMemory();
        _writingHistory = history;
        WritingPageControl.Initialize(_draft, writingCoach, () => SetupPageControl.RefreshFromDraft(_draft), history);
        WritingPageControl.GrammarTopicRequested += OnGrammarTopicRequested;
        WritingPageControl.GrammarLessonRequested += OnGrammarLessonRequested;
        WritingPageControl.DrillCompleted += OnDrillCompleted;
        WritingPageControl.GuidedCompleted += OnGuidedCompleted;
        GrammarPageControl.Initialize(_draft, grammarLibrary, history);
        GrammarPageControl.PracticeRequested += OnGrammarPracticeRequested;
        GrammarPageControl.QuizCompleted += OnQuizCompleted;
        RoadmapPageControl.Initialize(history);
        RoadmapPageControl.StartSessionRequested += OnStartSessionRequested;
        RoadmapPageControl.TopicRequested += OnGrammarLessonRequested;
        RoadmapPageControl.PracticeRequested += OnRoadmapPracticeRequested;
        UpdateApiKeyStatus();
        SyncNavigationSelection();
        UpdatePageHeader();
        RenderSelectedPage();
        RefreshUi();
    }

    private void OnPracticeModeChanged(PracticeMode mode) => _draft.SetPracticeMode(mode);

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

    private void OnLaunchAtWindowsSignInChanged(bool enabled) =>
        _draft.SetDisplayPreferences(
            DisplayPageControl.ShowSentenceOverlay,
            DisplayPageControl.ShowVocabularyCards,
            DisplayPageControl.ShowFloatingFlashcard,
            DisplayPageControl.ActiveFlashcardDeckKey,
            enabled);

    private void OnTtsPreferencesChanged(string voice, double speed) =>
        _draft.SetTtsPreferences(voice, speed);

    private void OnPracticeAudioDelayChanged(int seconds) =>
        _draft.SetPracticeAudioDelaySeconds(seconds);

    private void OnAiProviderChanged(AiProvider provider) => _draft.SetAiProvider(provider);

    private void OnClearProviderKeyRequested(AiProvider provider)
    {
        if (provider == AiProvider.Groq)
        {
            _groqKeyCleared = true;
            _groqKeyConfigured = false;
        }
        else if (provider == AiProvider.Claude)
        {
            _claudeKeyCleared = true;
            _claudeKeyConfigured = false;
        }

        UpdateApiKeyStatus();
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
            DisplayPageControl.LaunchAtWindowsSignInChanged -= OnLaunchAtWindowsSignInChanged;
            DisplayPageControl.TtsPreferencesChanged -= OnTtsPreferencesChanged;
            DisplayPageControl.PracticeAudioDelayChanged -= OnPracticeAudioDelayChanged;
        }
        if (GeminiPageControl is not null)
        {
            GeminiPageControl.ClearKeyRequested -= OnClearKeyRequested;
            GeminiPageControl.ProviderChanged -= OnAiProviderChanged;
            GeminiPageControl.ClearProviderKeyRequested -= OnClearProviderKeyRequested;
        }
        ReviewPracticePageControl.PracticeModeChanged -= OnPracticeModeChanged;
        ReviewPracticePageControl?.Dispose();
        FlashcardsPageControl?.Dispose();
        if (WritingPageControl is not null)
        {
            WritingPageControl.GrammarTopicRequested -= OnGrammarTopicRequested;
            WritingPageControl.GrammarLessonRequested -= OnGrammarLessonRequested;
            WritingPageControl.DrillCompleted -= OnDrillCompleted;
            WritingPageControl.GuidedCompleted -= OnGuidedCompleted;
            WritingPageControl.Dispose();
        }
        if (GrammarPageControl is not null)
        {
            GrammarPageControl.PracticeRequested -= OnGrammarPracticeRequested;
            GrammarPageControl.QuizCompleted -= OnQuizCompleted;
            GrammarPageControl.Dispose();
        }
        if (RoadmapPageControl is not null)
        {
            RoadmapPageControl.StartSessionRequested -= OnStartSessionRequested;
            RoadmapPageControl.TopicRequested -= OnGrammarLessonRequested;
            RoadmapPageControl.PracticeRequested -= OnRoadmapPracticeRequested;
            RoadmapPageControl.Dispose();
        }
        if (_roadmapSession is not null)
            _roadmapSession.Changed -= RenderSessionBanner;
    }

    /// <summary>Opens the Grammar page at the lesson for <paramref name="topicKey"/>.</summary>
    public void OpenGrammarTopic(string topicKey)
    {
        NavigateTo(SettingsPageId.Grammar);
        _ = GrammarPageControl.OpenTopicAsync(topicKey);
    }

    private void OnGrammarTopicRequested(string topicKey) => OpenGrammarTopic(topicKey);

    private void OnGrammarLessonRequested(GrammarTopic topic)
    {
        NavigateTo(SettingsPageId.Grammar);
        _ = GrammarPageControl.OpenTopicAsync(topic);
    }

    private void OnGrammarPracticeRequested(GrammarTopic topic)
    {
        // During today's session a drill of a planned topic uses the stage's exercise.
        var planned = _roadmapSession?.Steps.FirstOrDefault(step => step.Kind == RoadmapStepKind.Drill && step.Topic?.Key == topic.Key);
        NavigateTo(SettingsPageId.Writing);
        _ = WritingPageControl.StartDrillAsync(topic, planned?.Practice);
    }

    // ---- Roadmap session ------------------------------------------------------------------------

    /// <summary>Starts today's roadmap session, or resumes the one that is running.</summary>
    public void StartRoadmapSession()
    {
        if (_roadmapSession is { IsFinished: false })
        {
            RunCurrentStep();
            return;
        }

        var plan = _writingHistory.TodayPlan();
        if (plan.Count == 0)
            return;

        _roadmapSession = new RoadmapSession(_writingHistory.CurrentRoadmapStage(), plan, _writingHistory.Now);
        _roadmapSession.Changed += RenderSessionBanner;
        RoadmapPageControl.HideSummary();
        RoadmapPageControl.IsSessionActive = true;
        RunCurrentStep();
    }

    /// <summary>Moves to the next step of today's session, or finishes it after the last step.</summary>
    public void NextRoadmapStep()
    {
        if (_roadmapSession is not { IsFinished: false } session)
            return;

        var left = session.Current;
        var leftDone = session.IsCurrentDone;
        if (!session.Advance())
        {
            FinishRoadmapSession();
            return;
        }

        // A skipped writing step should not keep counting sentences on the Writing page.
        if (!leftDone && left.Kind != RoadmapStepKind.Learn)
            WritingPageControl.EndDrill();
        RunCurrentStep();
    }

    /// <summary>Ends today's session and shows its summary on the Roadmap page.</summary>
    public void FinishRoadmapSession()
    {
        if (_roadmapSession is not { } session)
            return;

        session.Changed -= RenderSessionBanner;
        session.Finish();
        _roadmapSession = null;
        RoadmapPageControl.IsSessionActive = false;
        RoadmapPageControl.ShowSummary(session.Summarize(_writingHistory.Current, _writingHistory.Now));
        RenderSessionBanner();
        NavigateTo(SettingsPageId.Roadmap);
    }

    /// <summary>Opens the page of the current step and starts it, unless it is done or already running.</summary>
    private void RunCurrentStep()
    {
        if (_roadmapSession is not { IsFinished: false } session)
            return;

        var step = session.Current;
        RenderSessionBanner();
        var running = step.Kind switch
        {
            RoadmapStepKind.Learn => GrammarPageControl.CurrentTopic?.Key == step.Topic?.Key && GrammarPageControl.IsLessonOpen,
            RoadmapStepKind.Drill => WritingPageControl.Drill is { IsComplete: false } drill && drill.Topic.Key == step.Topic?.Key,
            _ => WritingPageControl.Guided is { IsComplete: false } run && run.Kind == step.Kind
        };

        NavigateTo(step.Kind == RoadmapStepKind.Learn ? SettingsPageId.Grammar : SettingsPageId.Writing);
        if (session.IsCurrentDone || running)
            return;

        switch (step.Kind)
        {
            case RoadmapStepKind.Learn when step.Topic is not null:
                _ = GrammarPageControl.OpenTopicAsync(step.Topic);
                break;
            case RoadmapStepKind.Drill when step.Topic is not null:
                _ = WritingPageControl.StartDrillAsync(step.Topic, step.Practice);
                break;
            case RoadmapStepKind.Review or RoadmapStepKind.Apply:
                _ = WritingPageControl.StartGuidedAsync(GuidedPractice.ForStep(step));
                break;
        }
    }

    private void RenderSessionBanner()
    {
        if (SessionBanner is null)
            return;

        if (_roadmapSession is not { IsFinished: false } session)
        {
            SessionBanner.Visibility = Visibility.Collapsed;
            return;
        }

        SessionBanner.Visibility = Visibility.Visible;
        SessionGlyph.Text = RoadmapPresentation.StepGlyph(session.Current.Kind);
        SessionBannerText.Text = RoadmapPresentation.BannerText(session);
        SessionNextButton.Content = RoadmapPresentation.NextButtonText(session);
        // The next step stands out once the current one is done.
        if (session.IsCurrentDone)
            SessionNextButton.ClearValue(StyleProperty);
        else
            SessionNextButton.Style = (Style)FindResource("SecondaryButton");
    }

    private void OnStartSessionRequested() => StartRoadmapSession();

    private void OnDrillCompleted(GrammarDrill drill) => _roadmapSession?.Complete(RoadmapStepKind.Drill, drill.Topic.Key);

    private void OnGuidedCompleted(GuidedPractice run) => _roadmapSession?.Complete(run.Kind, run.Topic?.Key);

    private void OnQuizCompleted(GrammarTopic topic, int correct, int total) => _roadmapSession?.Complete(RoadmapStepKind.Learn, topic.Key);

    private void OnRoadmapPracticeRequested(RoadmapStage stage, RoadmapPractice practice)
    {
        NavigateTo(SettingsPageId.Writing);
        _ = WritingPageControl.StartGuidedAsync(new GuidedPractice(
            $"Stage {stage.Number} practice",
            practice,
            RoadmapRules.ApplySentences));
    }

    private void SessionNextButton_Click(object sender, RoutedEventArgs e) => NextRoadmapStep();

    private void SessionEndButton_Click(object sender, RoutedEventArgs e) => FinishRoadmapSession();

    private void SessionRoadmapButton_Click(object sender, RoutedEventArgs e) => NavigateTo(SettingsPageId.Roadmap);

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
        _groqKeyConfigured = SaveProviderKey(_groqKeyStore, GeminiPageControl.CurrentGroqKey, _groqKeyCleared, _groqKeyConfigured);
        _claudeKeyConfigured = SaveProviderKey(_claudeKeyStore, GeminiPageControl.CurrentClaudeKey, _claudeKeyCleared, _claudeKeyConfigured);
        _draft.SetProviderKeyConfigured(AiProvider.Groq, _groqKeyConfigured);
        _draft.SetProviderKeyConfigured(AiProvider.Claude, _claudeKeyConfigured);
        _draft.SetAiProvider(GeminiPageControl.SelectedProvider);
        if (DisplayPageControl is not null)
        {
            _draft.SetDisplayPreferences(
                DisplayPageControl.ShowSentenceOverlay,
                DisplayPageControl.ShowVocabularyCards,
                DisplayPageControl.ShowFloatingFlashcard,
                DisplayPageControl.ActiveFlashcardDeckKey,
                DisplayPageControl.LaunchAtWindowsSignIn);
            _draft.SetTtsPreferences(
                DisplayPageControl.TtsVoice,
                DisplayPageControl.TtsSpeed);
            _draft.SetPracticeAudioDelaySeconds(DisplayPageControl.PracticeAudioDelaySeconds);
        }
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

    /// <summary>Saves a typed key, clears a key the user removed, and returns whether a key is now stored.</summary>
    private static bool SaveProviderKey(ProtectedApiKeyStore? store, string? typedKey, bool cleared, bool configured)
    {
        if (!string.IsNullOrEmpty(typedKey))
        {
            store?.Save(typedKey);
            return true;
        }

        if (cleared)
        {
            store?.Clear();
            return false;
        }

        return configured;
    }

    private void UpdateApiKeyStatus()
    {
        GeminiPageControl?.LoadState(_apiKeyConfigured);
        GeminiPageControl?.LoadProviderState(_draft.Settings.AiProvider, _groqKeyConfigured, _claudeKeyConfigured);
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
            DisplayPageControl?.LoadSettings(_draft.Settings);
            ReviewPageControl?.LoadData(new ReviewViewModel(_draft.Settings));
            GeminiPageControl?.LoadState(_apiKeyConfigured);
            GeminiPageControl?.LoadProviderState(_draft.Settings.AiProvider, _groqKeyConfigured, _claudeKeyConfigured);
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
