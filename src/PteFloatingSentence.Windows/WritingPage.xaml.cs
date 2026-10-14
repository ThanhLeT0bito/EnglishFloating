using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using PteFloatingSentence.Core;
using PteFloatingSentence.Windows.Infrastructure;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Button = System.Windows.Controls.Button;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using ComboBoxItem = System.Windows.Controls.ComboBoxItem;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using Orientation = System.Windows.Controls.Orientation;
using RadioButton = System.Windows.Controls.RadioButton;
using TextBox = System.Windows.Controls.TextBox;
using UserControl = System.Windows.Controls.UserControl;

namespace PteFloatingSentence.Windows;

/// <summary>
/// Settings page for Vietnamese → English translation practice. Preferences live in the Settings draft;
/// the practice flow is driven by <see cref="WritingSession"/>.
/// </summary>
public partial class WritingPage : UserControl, IDisposable
{
    public const int RecentMistakeRows = 8;

    private readonly ISentenceAudioPlayback _audio;
    private StudyListDraft? _draft;
    private WritingSession? _session;
    private WritingHistoryBook _history = WritingHistoryBook.InMemory();
    private bool _mistakesCollapsed = true;
    private bool _tipsCollapsed;
    private bool _clearArmed;
    private GrammarDrill? _drill;
    private Action? _onDraftChanged;
    private bool _rendering;
    private bool _disposed;

    /// <summary>Setup options are folded under the arrow header.</summary>
    private bool _setupCollapsed;

    public WritingPage() : this(new SentenceAudioPlayback(new WpfAudioPlayer(), new EdgeNeuralTtsService(), new AudioCacheManager()))
    {
    }

    public WritingPage(ISentenceAudioPlayback audio)
    {
        _audio = audio ?? throw new ArgumentNullException(nameof(audio));
        InitializeComponent();
        _audio.StateChanged += Audio_StateChanged;
        PreviewKeyDown += WritingPage_PreviewKeyDown;
        IsVisibleChanged += WritingPage_IsVisibleChanged;
        RetypeCheckBox.ToolTip = WritingPresentation.RetypeOptionHelp;
        AutoPlayCheckBox.ToolTip = WritingPresentation.AutoPlayOptionHelp;
        WeakPointsCheckBox.ToolTip = WritingPresentation.WeakPointsOptionHelp;
    }

    /// <summary>The practice flow, or null when no AI coach is available.</summary>
    public WritingSession? Session => _session;

    /// <summary>The mistake notebook shown on this page.</summary>
    public WritingHistoryBook History => _history;

    /// <summary>The grammar drill in progress (or just finished), or null.</summary>
    public GrammarDrill? Drill => _drill;

    /// <summary>Asks the host to open the lesson for a grammar topic key.</summary>
    public event Action<string>? GrammarTopicRequested;

    /// <summary>Asks the host to open the lesson of the drill topic (custom topics included).</summary>
    public event Action<GrammarTopic>? GrammarLessonRequested;

    public void Initialize(StudyListDraft draft, IWritingCoach? coach, Action? onDraftChanged = null, WritingHistoryBook? history = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _draft = draft ?? throw new ArgumentNullException(nameof(draft));
        _onDraftChanged = onDraftChanged;
        _history.Changed -= History_Changed;
        _history = history ?? WritingHistoryBook.InMemory();
        _history.Changed += History_Changed;
        if (_session is not null)
        {
            _session.StateChanged -= Session_StateChanged;
            _session.Dispose();
            _session = null;
        }

        if (coach is not null)
        {
            _session = new WritingSession(coach, prefetchNext: true) { History = _history };
            _session.StateChanged += Session_StateChanged;
        }

        RefreshFromDraft();
        RenderSession();
        RenderMistakes();
    }

    private void History_Changed()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.InvokeAsync(History_Changed);
            return;
        }

        if (_disposed)
            return;

        RenderMistakes();
        var wasRendering = _rendering;
        _rendering = true;
        try
        {
            RenderWeakPoints();
            RenderSetupCard();
        }
        finally
        {
            _rendering = wasRendering;
        }
    }

    private void ApplySessionOptions()
    {
        if (_session is null || _draft is null)
            return;

        var mode = _draft.Settings.WritingPracticeMode;
        // A drill needs English answers checked by the AI.
        _session.Mode = _drill is { IsComplete: false } ? GrammarDrill.ModeFor(mode) : mode;
        _session.RetypeEnabled = _draft.Settings.WritingRetypeEnabled;
    }

    private IReadOnlyList<WritingSkillTag> FocusTags() =>
        _draft is { Settings.WritingFocusWeakPoints: true } && _history.CanPracticeWeakPoints ? _history.WeakTags() : [];

    /// <summary>Re-reads topics, level, language and study lists from the draft.</summary>
    public void RefreshFromDraft()
    {
        if (_draft is null || _disposed)
            return;

        _rendering = true;
        try
        {
            RenderTopics();
            var settings = _draft.Settings;
            EasyLevelButton.IsChecked = settings.WritingLevel == WritingLevel.Easy;
            MediumLevelButton.IsChecked = settings.WritingLevel == WritingLevel.Medium;
            HardLevelButton.IsChecked = settings.WritingLevel == WritingLevel.Hard;
            EnglishExplanationButton.IsChecked = settings.WritingExplanationLanguage == WritingExplanationLanguage.English;
            VietnameseExplanationButton.IsChecked = settings.WritingExplanationLanguage == WritingExplanationLanguage.Vietnamese;
            TranslateModeButton.IsChecked = settings.WritingPracticeMode == WritingPracticeMode.Translate;
            DictationModeButton.IsChecked = settings.WritingPracticeMode == WritingPracticeMode.Dictation;
            ParaphraseModeButton.IsChecked = settings.WritingPracticeMode == WritingPracticeMode.Paraphrase;
            CombineModeButton.IsChecked = settings.WritingPracticeMode == WritingPracticeMode.Combine;
            ReverseModeButton.IsChecked = settings.WritingPracticeMode == WritingPracticeMode.Reverse;
            RetypeCheckBox.IsChecked = settings.WritingRetypeEnabled;
            AutoPlayCheckBox.IsChecked = settings.WritingAutoPlayCorrected;
            RenderWeakPoints();
            RenderSaveListSelector();
            RenderSetupCard();
        }
        finally
        {
            _rendering = false;
        }
    }

    /// <summary>
    /// Folds or unfolds the setup options under the arrow header. Folded, the header shows a one-line summary;
    /// Generate stays in the header so a sentence can always be requested.
    /// </summary>
    private void RenderSetupCard()
    {
        if (_draft is null)
            return;

        SetupExpandedPanel.Visibility = _setupCollapsed ? Visibility.Collapsed : Visibility.Visible;
        SetupSummaryText.Visibility = _setupCollapsed ? Visibility.Visible : Visibility.Collapsed;
        SetupChevron.Text = _setupCollapsed ? "\uE76C" : "\uE70D";
        SetupToggleButton.ToolTip = _setupCollapsed ? "Show options" : "Hide options";

        var settings = _draft.Settings;
        SetupSummaryText.Text = WritingPresentation.SetupSummary(
            WritingTopicRules.SelectedTopics(settings).Select(topic => topic.Name).ToList(),
            settings.WritingLevel,
            settings.WritingExplanationLanguage,
            settings.WritingPracticeMode,
            settings.WritingFocusWeakPoints && _history.CanPracticeWeakPoints);
    }

    private void RenderWeakPoints()
    {
        if (_draft is null)
            return;

        var available = _history.CanPracticeWeakPoints;
        WeakPointsCheckBox.IsEnabled = available;
        WeakPointsCheckBox.IsChecked = available && _draft.Settings.WritingFocusWeakPoints;
        WeakPointsText.Text = WritingPresentation.WeakPointsText(available, _history.WeakTags(), _history.RecentMistakeCount);
    }

    private void ModeButton_Checked(object sender, RoutedEventArgs e)
    {
        if (_rendering || _draft is null)
            return;

        var mode = ModeFor(sender);
        if (mode == _draft.Settings.WritingPracticeMode)
            return;

        _draft.SetWritingPracticeMode(mode);
        ApplySessionOptions();
        RenderSetupCard();
        RenderSession();
        if (_session is null)
            return;

        // A paraphrase or combine exercise needs its own kind of sentence: get one right away.
        if (_session.PromptNeedsNewMode || (_session.State == WritingSessionState.Generating && _session.Prompt is null))
            _ = GenerateAsync();
        else if (mode == WritingPracticeMode.Dictation && _session is { State: WritingSessionState.Answering, Prompt: not null } session)
            _ = PlayAsync(session.Prompt.ReferenceEnglish, restart: true);
    }

    private WritingPracticeMode ModeFor(object sender) =>
        ReferenceEquals(sender, DictationModeButton) ? WritingPracticeMode.Dictation
        : ReferenceEquals(sender, ParaphraseModeButton) ? WritingPracticeMode.Paraphrase
        : ReferenceEquals(sender, CombineModeButton) ? WritingPracticeMode.Combine
        : ReferenceEquals(sender, ReverseModeButton) ? WritingPracticeMode.Reverse
        : WritingPracticeMode.Translate;

    /// <summary>English sentence to play or save after a check (the English source when translating into Vietnamese).</summary>
    private string? EnglishResult() =>
        _session is { Prompt: { } prompt, Feedback: { } feedback } session
            ? WritingModes.EnglishResult(prompt, feedback, session.FeedbackMode)
            : null;

    private void PracticeOption_Click(object sender, RoutedEventArgs e)
    {
        if (_rendering || _draft is null)
            return;

        var settings = _draft.Settings;
        _draft.SetWritingOptions(
            RetypeCheckBox.IsChecked == true,
            AutoPlayCheckBox.IsChecked == true,
            WeakPointsCheckBox.IsEnabled ? WeakPointsCheckBox.IsChecked == true : settings.WritingFocusWeakPoints);
        ApplySessionOptions();
        RenderSetupCard();
    }

    /// <summary>True when the setup options are folded under the header.</summary>
    public bool IsSetupCollapsed => SetupExpandedPanel.Visibility != Visibility.Visible;

    private void SetupToggleButton_Click(object sender, RoutedEventArgs e)
    {
        _setupCollapsed = !_setupCollapsed;
        RenderSetupCard();
    }

    // ---- Preferences -------------------------------------------------------------------------

    private void RenderTopics()
    {
        if (_draft is null)
            return;

        TopicPanel.Children.Clear();
        var selected = _draft.Settings.SelectedWritingTopicKeys.ToHashSet(StringComparer.Ordinal);
        foreach (var topic in WritingTopicRules.AllTopics(_draft.Settings))
        {
            var chip = new ToggleButton
            {
                Content = topic.Name,
                Tag = topic.Key,
                IsChecked = selected.Contains(topic.Key),
                Style = (Style)FindResource("TopicChipStyle")
            };
            chip.Click += TopicChip_Click;

            if (!WritingTopics.IsCustomKey(topic.Key))
            {
                chip.Margin = new Thickness(0, 0, 8, 8);
                TopicPanel.Children.Add(chip);
                continue;
            }

            var remove = new Button { Tag = topic.Key, Style = (Style)FindResource("ChipRemoveButtonStyle") };
            remove.Click += RemoveTopicButton_Click;
            var group = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 8, 8) };
            group.Children.Add(chip);
            group.Children.Add(remove);
            TopicPanel.Children.Add(group);
        }
    }

    private void TopicChip_Click(object sender, RoutedEventArgs e)
    {
        if (_rendering || _draft is null || sender is not ToggleButton { Tag: string key } chip)
            return;

        var isSelected = _draft.Settings.SelectedWritingTopicKeys.Contains(key, StringComparer.Ordinal);
        if (isSelected != (chip.IsChecked == true))
            _draft.ToggleWritingTopic(key);
    }

    private void RemoveTopicButton_Click(object sender, RoutedEventArgs e)
    {
        if (_draft is null || sender is not Button { Tag: string key })
            return;

        _draft.RemoveWritingTopic(key);
        TopicErrorText.Text = string.Empty;
        _rendering = true;
        try
        {
            RenderTopics();
        }
        finally
        {
            _rendering = false;
        }
    }

    private void AddTopicButton_Click(object sender, RoutedEventArgs e) => AddTopic();

    private void AddTopic()
    {
        if (_draft is null)
            return;

        var keysBefore = _draft.Settings.CustomWritingTopics.Select(topic => topic.Key).ToHashSet(StringComparer.Ordinal);
        var result = _draft.AddWritingTopic(NewTopicInput.Text);
        if (!result.IsValid)
        {
            TopicErrorText.Text = result.Error ?? string.Empty;
            return;
        }

        var added = _draft.Settings.CustomWritingTopics.FirstOrDefault(topic => !keysBefore.Contains(topic.Key));
        if (added is not null)
            _draft.ToggleWritingTopic(added.Key);

        NewTopicInput.Text = string.Empty;
        TopicErrorText.Text = string.Empty;
        _rendering = true;
        try
        {
            RenderTopics();
        }
        finally
        {
            _rendering = false;
        }
    }

    private void LevelButton_Checked(object sender, RoutedEventArgs e)
    {
        if (_rendering || _draft is null)
            return;

        var level = ReferenceEquals(sender, EasyLevelButton) ? WritingLevel.Easy
            : ReferenceEquals(sender, HardLevelButton) ? WritingLevel.Hard
            : WritingLevel.Medium;
        _draft.SetWritingLevel(level);
    }

    private void LanguageButton_Checked(object sender, RoutedEventArgs e)
    {
        if (_rendering || _draft is null)
            return;

        _draft.SetWritingExplanationLanguage(ReferenceEquals(sender, VietnameseExplanationButton)
            ? WritingExplanationLanguage.Vietnamese
            : WritingExplanationLanguage.English);
    }

    private void RenderSaveListSelector()
    {
        if (_draft is null)
            return;

        var previous = (SaveListSelector.SelectedItem as ComboBoxItem)?.Tag as Guid?;
        SaveListSelector.Items.Clear();
        foreach (var list in _draft.Settings.StudyLists)
            SaveListSelector.Items.Add(new ComboBoxItem { Content = list.Name, Tag = list.Id });

        var target = previous ?? _draft.Settings.ActiveListId;
        SaveListSelector.SelectedItem = SaveListSelector.Items.OfType<ComboBoxItem>().FirstOrDefault(item => item.Tag is Guid id && id == target)
            ?? SaveListSelector.Items.OfType<ComboBoxItem>().FirstOrDefault();
    }

    // ---- Practice flow -----------------------------------------------------------------------

    private async void GenerateButton_Click(object sender, RoutedEventArgs e) => await GenerateAsync();

    private async void SkipButton_Click(object sender, RoutedEventArgs e) => await GenerateAsync();

    private async void NextButton_Click(object sender, RoutedEventArgs e) => await GenerateAsync();

    private async void CheckButton_Click(object sender, RoutedEventArgs e)
    {
        if (_session is { IsRetyping: true })
            SubmitRetype();
        else
            await CheckAsync();
    }

    private void SkipRetypeButton_Click(object sender, RoutedEventArgs e)
    {
        _session?.SkipRetype();
        AnswerBox.Focus();
    }

    private async void ListenButton_Click(object sender, RoutedEventArgs e) => await PlayDictationAsync(slow: false);

    private async void SlowListenButton_Click(object sender, RoutedEventArgs e) => await PlayDictationAsync(slow: true);

    private async Task PlayDictationAsync(bool slow)
    {
        var reference = _session is { Prompt: { } prompt } session ? WritingModes.ListenText(prompt, session.PromptMode) : null;
        AnswerBox.Focus();
        if (!string.IsNullOrWhiteSpace(reference))
            await PlayAsync(reference, restart: true, slow: slow);
    }

    /// <summary>Plays <paramref name="text"/>; with <paramref name="restart"/> it starts over instead of toggling.</summary>
    private async Task PlayAsync(string text, bool restart = false, bool slow = false)
    {
        if (_disposed || _draft is null || string.IsNullOrWhiteSpace(text))
            return;

        var settings = _draft.Settings;
        var voice = string.IsNullOrWhiteSpace(settings.TtsVoice) ? EdgeNeuralTtsService.DefaultVoice : settings.TtsVoice;
        var normal = WritingPresentation.NormalSpeed(settings.TtsSpeed);
        if (restart)
            _audio.Stop();
        await _audio.ToggleAsync(text, voice, slow ? WritingPresentation.SlowSpeed(normal) : normal);
    }

    /// <summary>Checks the retyped corrected sentence; a match plays it once more.</summary>
    public void SubmitRetype()
    {
        var session = _session;
        if (_disposed || session is null || !session.IsRetyping)
            return;

        var result = session.SubmitRetype(AnswerBox.Text);
        if (result is { IsMatch: true } && EnglishResult() is { } english)
            _ = PlayAsync(english, restart: true);
        AnswerBox.Focus();
    }

    private async void RetryButton_Click(object sender, RoutedEventArgs e)
    {
        if (_session is not null)
            await _session.RetryAsync();
    }

    private void TipButton_Click(object sender, RoutedEventArgs e) => RevealTip();

    /// <summary>True when revealed tips are folded under the "Tips" header.</summary>
    public bool IsTipsCollapsed => TipsBody.Visibility != Visibility.Visible;

    /// <summary>Reveals the next tip and unfolds the tips so it can be read.</summary>
    private void RevealTip()
    {
        if (_session is null || _session.PromptMode == WritingPracticeMode.Dictation)
            return;

        var wasCollapsed = _tipsCollapsed;
        _tipsCollapsed = false;
        if (!_session.RevealNextTip() && wasCollapsed)
            RenderSession();
    }

    private void TipsToggleButton_Click(object sender, RoutedEventArgs e)
    {
        _tipsCollapsed = !_tipsCollapsed;
        RenderSession();
    }

    private void RenderTipsHeader(WritingSession session)
    {
        TipsBody.Visibility = _tipsCollapsed ? Visibility.Collapsed : Visibility.Visible;
        TipsChevron.Text = _tipsCollapsed ? "\uE76C" : "\uE70D";
        TipsToggleButton.ToolTip = _tipsCollapsed ? "Show tips" : "Hide tips";
        TipsSummaryText.Text = WritingPresentation.TipsSummary(session.RevealedTipTiers);
    }

    /// <summary>Asks the coach for a new sentence using the current topics, level and language.</summary>
    public async Task GenerateAsync()
    {
        if (_disposed || _draft is null)
            return;

        if (_session is null)
        {
            ShowStatus(WritingPresentation.NoCoachMessage, isError: true, canRetry: false);
            return;
        }

        _audio.Stop();
        AnswerBox.Text = string.Empty;
        SaveStatusText.Visibility = Visibility.Collapsed;
        var settings = _draft.Settings;
        var topics = WritingTopicRules.SelectedTopics(settings).Select(topic => topic.Name).ToList();
        if (_drill is { IsComplete: true })
        {
            // The next sentence after a finished drill is a normal one.
            _drill = null;
            RenderDrill();
        }

        ApplySessionOptions();
        var drillTopic = _drill?.Topic;
        await _session.GenerateAsync(topics, settings.WritingLevel, settings.WritingExplanationLanguage, drillTopic is null ? FocusTags() : [], drillTopic);
        if (_session is { State: WritingSessionState.Answering } session)
        {
            // Options are set; fold them away so the sentence and answer get the space.
            _setupCollapsed = true;
            RenderSetupCard();
            AnswerBox.Focus();
            if (session.Mode == WritingPracticeMode.Dictation && session.Prompt is not null)
                await PlayAsync(session.Prompt.ReferenceEnglish, restart: true);
        }
    }

    /// <summary>Checks the text in the answer box.</summary>
    public async Task CheckAsync()
    {
        if (_disposed || _session is null || _draft is null)
            return;

        _audio.Stop();
        ApplySessionOptions();
        await _session.CheckAsync(AnswerBox.Text, _draft.Settings.WritingExplanationLanguage);
        if (_session is not { State: WritingSessionState.Reviewed, Feedback: not null } session)
            return;

        RecordDrillCheck(session);
        if (session.IsRetyping)
        {
            // The answer stays highlighted in the result; the box is now for typing the corrected sentence.
            AnswerBox.Text = string.Empty;
            AnswerBox.Focus();
        }
        else if (_draft.Settings.WritingAutoPlayCorrected && EnglishResult() is { } english)
        {
            await PlayAsync(english, restart: true);
        }
    }

    // ---- Grammar drill ---------------------------------------------------------------------------

    /// <summary>Starts a drill of <see cref="GrammarProgressRules.DrillLength"/> sentences on <paramref name="topic"/>.</summary>
    public async Task StartDrillAsync(GrammarTopic topic)
    {
        ArgumentNullException.ThrowIfNull(topic);
        if (_disposed)
            return;

        _drill = new GrammarDrill(topic);
        RenderDrill();
        await GenerateAsync();
    }

    /// <summary>Stops the drill; the next sentence is a normal one.</summary>
    public void EndDrill()
    {
        _drill = null;
        ApplySessionOptions();
        RenderDrill();
        RenderSession();
    }

    private void RecordDrillCheck(WritingSession session)
    {
        if (_drill is not { IsComplete: false } drill || session.Prompt is not { } prompt || session.Feedback is not { } feedback)
            return;
        // Only sentences written for this drill count.
        if (!string.Equals(prompt.FocusTopicKey, drill.Topic.Key, StringComparison.Ordinal))
            return;

        if (drill.Record(prompt, feedback))
            _history.RecordDrill(drill.ToResult(_history.Now));
        RenderDrill();
    }

    private void RenderDrill()
    {
        if (_disposed)
            return;

        var drill = _drill;
        DrillBanner.Visibility = drill is null ? Visibility.Collapsed : Visibility.Visible;
        if (drill is null)
            return;

        DrillProgressText.Text = drill.IsComplete ? $"Drill · {drill.Topic.Name}" : GrammarPresentation.DrillProgress(drill);
        DrillSummaryPanel.Visibility = drill.IsComplete ? Visibility.Visible : Visibility.Collapsed;
        DrillSummaryText.Text = drill.IsComplete ? GrammarPresentation.DrillSummary(drill) : string.Empty;
        EndDrillButton.Content = drill.IsComplete ? "Close" : "End drill";
    }

    private void EndDrillButton_Click(object sender, RoutedEventArgs e) => EndDrill();

    private async void DrillAgainButton_Click(object sender, RoutedEventArgs e)
    {
        if (_drill is not null)
            await StartDrillAsync(_drill.Topic);
    }

    private void DrillLessonButton_Click(object sender, RoutedEventArgs e)
    {
        if (_drill is not null)
            GrammarLessonRequested?.Invoke(_drill.Topic);
    }

    private void GrammarHintButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string key })
            GrammarTopicRequested?.Invoke(key);
    }

    private void GrammarReviewButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string key })
            GrammarTopicRequested?.Invoke(key);
    }

    private void WritingPage_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var ctrl = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;
        var shift = (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;

        if (ctrl && e.Key == Key.H)
        {
            e.Handled = true;
            RevealTip();
            return;
        }

        if (ctrl && e.Key == Key.Space && _session is { Prompt: not null } && WritingPresentation.ShowsListenBar(_session.PromptMode))
        {
            e.Handled = true;
            _ = PlayDictationAsync(slow: shift);
            return;
        }

        if (e.Key != Key.Enter && e.Key != Key.Return)
            return;

        if (ReferenceEquals(e.OriginalSource, NewTopicInput))
        {
            // Keep Enter away from the Settings window's default Save button.
            e.Handled = true;
            AddTopic();
            return;
        }

        if (ReferenceEquals(e.OriginalSource, AnswerBox) && (ctrl || !shift))
        {
            e.Handled = true;
            _ = HandleAnswerEnterAsync();
        }
    }

    private async Task HandleAnswerEnterAsync()
    {
        if (_session is null || _session.IsBusy)
            return;

        switch (_session.EnterActionFor(AnswerBox.Text))
        {
            case WritingEnterAction.Next:
                await GenerateAsync();
                break;
            case WritingEnterAction.Retype:
                SubmitRetype();
                break;
            case WritingEnterAction.Check:
                await CheckAsync();
                break;
        }
    }

    private void Session_StateChanged()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.InvokeAsync(Session_StateChanged);
            return;
        }

        RenderSession();
    }

    private void RenderSession()
    {
        if (_disposed)
            return;

        var session = _session;
        GenerateButton.Content = session?.Prompt is null ? "Generate sentence" : "New sentence";
        RenderSetupCard();

        if (session is null)
        {
            ShowStatus(WritingPresentation.NoCoachMessage, isError: true, canRetry: false);
            PromptCard.Visibility = Visibility.Collapsed;
            AnswerSection.Visibility = Visibility.Collapsed;
            ResultCard.Visibility = Visibility.Collapsed;
            return;
        }

        var busy = WritingPresentation.BusyText(session.State);
        if (busy is not null)
            ShowStatus(busy, isError: false, canRetry: false);
        else if (!string.IsNullOrEmpty(session.Error))
            ShowStatus(session.Error, isError: true, canRetry: session.CanRetry);
        else
            StatusPanel.Visibility = Visibility.Collapsed;

        RenderPrompt(session);

        var hasPrompt = session.Prompt is not null;
        var mode = session.PromptMode;
        AnswerSection.Visibility = hasPrompt ? Visibility.Visible : Visibility.Collapsed;
        AnswerBox.IsReadOnly = session.State == WritingSessionState.Checking;
        AnswerLabelText.Text = WritingPresentation.AnswerLabel(mode, session.IsRetyping);
        AnswerHintText.Text = WritingPresentation.AnswerHint(mode, session.IsRetyping);
        CheckButton.IsEnabled = hasPrompt && session.State is WritingSessionState.Answering or WritingSessionState.Reviewed;
        CheckButton.Content = WritingPresentation.CheckButtonText(session.State, session.IsRetyping);
        RenderRetype(session);
        RenderDrill();

        RenderFeedback(session);
    }

    private void RenderRetype(WritingSession session)
    {
        var show = session.State == WritingSessionState.Reviewed && session.RetypeTarget is not null && !session.RetypeSkipped;
        RetypePanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (!show)
            return;

        RetypeMessageText.Text = WritingPresentation.RetypeMessage(session.LastRetype);
        RetypeMessageText.Foreground = session.LastRetype is { IsMatch: false }
            ? new SolidColorBrush(Color.FromRgb(0xFC, 0xA5, 0xA5))
            : session.RetypeDone
                ? new SolidColorBrush(Color.FromRgb(0x34, 0xD3, 0x99))
                : new SolidColorBrush(Color.FromRgb(0x93, 0xC5, 0xFD));
        SkipRetypeButton.Visibility = session.IsRetyping ? Visibility.Visible : Visibility.Collapsed;
    }

    // ---- Mistake notebook ----------------------------------------------------------------------

    /// <summary>True when the "My mistakes" card is folded.</summary>
    public bool IsMistakesCollapsed => MistakesBody.Visibility != Visibility.Visible;

    private void MistakesToggleButton_Click(object sender, RoutedEventArgs e)
    {
        _mistakesCollapsed = !_mistakesCollapsed;
        RenderMistakes();
    }

    private void ClearHistoryButton_Click(object sender, RoutedEventArgs e)
    {
        // Two clicks: the first arms the button so a stray click cannot wipe the notebook.
        if (!_clearArmed)
        {
            _clearArmed = true;
            ClearHistoryButton.Content = "Click again to clear";
            return;
        }

        _clearArmed = false;
        _history.Clear();
    }

    private void RenderMistakes()
    {
        if (_disposed)
            return;

        _clearArmed = false;
        ClearHistoryButton.Content = "Clear history";
        MistakesBody.Visibility = _mistakesCollapsed ? Visibility.Collapsed : Visibility.Visible;
        MistakesChevron.Text = _mistakesCollapsed ? "\uE76C" : "\uE70D";
        MistakesToggleButton.ToolTip = _mistakesCollapsed ? "Show my mistakes" : "Hide my mistakes";

        var now = _history.Now;
        var stats = _history.TagStats();
        var count = _history.RecentMistakeCount;
        MistakesSummaryText.Text = count == 0
            ? "No mistakes yet"
            : $"{count} in 30 days · Most often: {WritingSkillTags.Label(stats[0].Tag)}";

        TagStatsPanel.Children.Clear();
        foreach (var stat in stats)
        {
            TagStatsPanel.Children.Add(new Border
            {
                Background = stat.Weight > 0 ? new SolidColorBrush(Color.FromArgb(0x26, 0xF8, 0x71, 0x71)) : new SolidColorBrush(Color.FromArgb(0x26, 0x34, 0xD3, 0x99)),
                BorderBrush = stat.Weight > 0 ? new SolidColorBrush(Color.FromArgb(0x66, 0xF8, 0x71, 0x71)) : new SolidColorBrush(Color.FromArgb(0x66, 0x34, 0xD3, 0x99)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(10, 3, 10, 3),
                Margin = new Thickness(0, 0, 8, 8),
                Child = new TextBlock { Text = WritingPresentation.TagStatText(stat), FontSize = 12.5, Foreground = (Brush)FindResource("TextSecondaryBrush") }
            });
        }

        GrammarReviewPanel.Children.Clear();
        var review = _history.GrammarRecommendations();
        GrammarReviewLabel.Visibility = review.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        foreach (var stat in review)
            GrammarReviewPanel.Children.Add(CreateGrammarReviewRow(stat));
        if (review.Count > 0)
            MistakesSummaryText.Text += $" · Review: {review[0].Topic.Name}";

        var recent = WritingHistoryRules.RecentMistakeGroups(_history.Current, RecentMistakeRows);
        NoMistakesText.Visibility = recent.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        RecentMistakesLabel.Visibility = recent.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        ClearHistoryButton.Visibility = _history.Current.Mistakes.Count == 0 && _history.Current.Reviews.Count == 0
            ? Visibility.Collapsed
            : Visibility.Visible;
        RecentMistakesPanel.Children.Clear();
        foreach (var group in recent)
            RecentMistakesPanel.Children.Add(CreateMistakeRow(group, now));
    }

    private UIElement CreateGrammarReviewRow(GrammarTopicStat stat)
    {
        var row = new Grid { Margin = new Thickness(0, 0, 0, 6) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var color = BrushFromHex(GrammarPresentation.StatusColor(stat.Status));
        var text = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock
        {
            Text = GrammarPresentation.TopicTitle(stat.Topic),
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("TextPrimaryBrush"),
            VerticalAlignment = VerticalAlignment.Center
        });
        text.Children.Add(new TextBlock
        {
            Text = $"  {GrammarPresentation.StatusLabel(stat.Status)} · {GrammarPresentation.StatLine(stat)}",
            FontSize = 12,
            Foreground = color,
            VerticalAlignment = VerticalAlignment.Center
        });
        row.Children.Add(text);

        var learn = new Button
        {
            Content = GrammarPresentation.LearnButtonText(stat.Topic),
            Tag = stat.Topic.Key,
            Style = (Style)FindResource("SecondaryButtonStyle"),
            Padding = new Thickness(10, 4, 10, 4),
            Margin = new Thickness(10, 0, 0, 0)
        };
        learn.Click += GrammarReviewButton_Click;
        Grid.SetColumn(learn, 1);
        row.Children.Add(learn);
        return row;
    }

    private UIElement CreateMistakeRow(WritingMistakeGroup group, DateTimeOffset now)
    {
        var mistake = group.Latest;
        var color = BrushFromHex(WritingPresentation.IssueColor(mistake.Type));
        var row = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.Children.Add(new Border
        {
            BorderBrush = color,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(8, 1, 8, 1),
            Margin = new Thickness(0, 1, 10, 0),
            HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Child = new TextBlock { Text = WritingSkillTags.Label(mistake.Tag), FontSize = 11, Foreground = color }
        });

        var details = new StackPanel();
        details.Children.Add(new TextBlock
        {
            Text = WritingPresentation.MistakeGroupText(group),
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("TextPrimaryBrush"),
            TextWrapping = TextWrapping.Wrap
        });
        var context = string.IsNullOrWhiteSpace(mistake.Explanation)
            ? WritingPresentation.RelativeDay(mistake.At, now)
            : $"{mistake.Explanation} · {WritingPresentation.RelativeDay(mistake.At, now)}";
        details.Children.Add(new TextBlock
        {
            Text = context,
            FontSize = 12,
            Foreground = (Brush)FindResource("TextMutedBrush"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 2, 0, 0),
            ToolTip = $"{mistake.Source}\n→ {mistake.Corrected}"
        });
        Grid.SetColumn(details, 1);
        row.Children.Add(details);
        return row;
    }

    private void ShowStatus(string text, bool isError, bool canRetry)
    {
        StatusText.Text = text;
        StatusText.Foreground = isError
            ? new SolidColorBrush(Color.FromRgb(0xFC, 0xA5, 0xA5))
            : (Brush)FindResource("TextMutedBrush");
        RetryButton.Visibility = canRetry ? Visibility.Visible : Visibility.Collapsed;
        StatusPanel.Visibility = Visibility.Visible;
    }

    private void RenderPrompt(WritingSession session)
    {
        var prompt = session.Prompt;
        if (prompt is null)
        {
            PromptCard.Visibility = Visibility.Collapsed;
            return;
        }

        PromptCard.Visibility = Visibility.Visible;
        PromptMetaText.Text = WritingPresentation.PromptMeta(prompt);
        var mode = session.PromptMode;
        var reviewed = session.State == WritingSessionState.Reviewed;
        var dictation = mode == WritingPracticeMode.Dictation;
        // In dictation the Vietnamese meaning stays hidden until the answer is checked.
        var hideMeaning = dictation && !reviewed;
        PromptHeaderText.Text = WritingPresentation.PromptHeader(mode);
        DictationBar.Visibility = WritingPresentation.ShowsListenBar(mode) ? Visibility.Visible : Visibility.Collapsed;
        DictationHintText.Text = hideMeaning ? WritingPresentation.DictationPromptText : string.Empty;
        VietnameseText.Visibility = hideMeaning ? Visibility.Collapsed : Visibility.Visible;
        TipButton.Visibility = dictation ? Visibility.Collapsed : Visibility.Visible;
        VietnameseText.Text = WritingModes.PromptText(prompt, mode);
        var instruction = WritingPresentation.InstructionText(prompt, mode);
        InstructionText.Text = instruction;
        InstructionText.Visibility = instruction.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        var meaning = reviewed ? WritingPresentation.MeaningLine(prompt, mode) : string.Empty;
        MeaningText.Text = meaning;
        MeaningText.Visibility = meaning.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        PromptCallText.Text = WritingPresentation.CallText(prompt.Call, session.PromptWasPrefetched);

        var available = session.AvailableTipTiers;
        TipButtonText.Text = WritingPresentation.TipButtonText(session.TipsRevealed, available.Count);
        TipButton.IsEnabled = session.CanRevealTip;

        var revealed = session.RevealedTipTiers;
        TipsPanel.Visibility = revealed.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        RenderTipsHeader(session);

        VocabularyTipSection.Visibility = revealed.Contains(WritingTipTier.Vocabulary) ? Visibility.Visible : Visibility.Collapsed;
        VocabularyTipList.Children.Clear();
        foreach (var hint in prompt.Tips.Vocabulary)
        {
            VocabularyTipList.Children.Add(new TextBlock
            {
                Text = "• " + WritingPresentation.VocabularyLine(hint, mode),
                FontSize = 13,
                Foreground = (Brush)FindResource("TextSecondaryBrush"),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 1, 0, 1)
            });
        }

        GrammarTipSection.Visibility = revealed.Contains(WritingTipTier.Grammar) ? Visibility.Visible : Visibility.Collapsed;
        GrammarTipText.Text = prompt.Tips.Grammar;
        StarterTipSection.Visibility = revealed.Contains(WritingTipTier.Starter) ? Visibility.Visible : Visibility.Collapsed;
        StarterTipText.Text = prompt.Tips.Starter + " …";
    }

    private void RenderFeedback(WritingSession session)
    {
        var feedback = session.Feedback;
        if (session.State != WritingSessionState.Reviewed || feedback is null)
        {
            ResultCard.Visibility = Visibility.Collapsed;
            return;
        }

        ResultCard.Visibility = Visibility.Visible;
        var verdictBrush = BrushFromHex(WritingPresentation.VerdictColor(feedback.Verdict));
        ScoreText.Text = feedback.Score.ToString(System.Globalization.CultureInfo.InvariantCulture);
        ScoreText.Foreground = verdictBrush;
        VerdictText.Text = WritingPresentation.VerdictLabel(feedback.Verdict);
        VerdictText.Foreground = verdictBrush;
        TipsUsedText.Text = WritingPresentation.TipsUsedText(session.TipsUsedForFeedback);
        TipsUsedText.Visibility = session.TipsUsedForFeedback > 0 ? Visibility.Visible : Visibility.Collapsed;
        SummaryText.Text = feedback.Summary;
        SummaryText.Visibility = string.IsNullOrWhiteSpace(feedback.Summary) ? Visibility.Collapsed : Visibility.Visible;

        var answer = session.LastAnswer ?? string.Empty;
        AnswerHighlightText.Inlines.Clear();
        var highlights = WritingFeedbackRules.MapHighlights(answer, feedback.Issues);
        foreach (var segment in WritingPresentation.SplitAnswer(answer, highlights))
        {
            var run = new Run(segment.Text);
            if (segment.IssueIndex is int index)
            {
                var issue = feedback.Issues[index];
                var color = (Color)ColorConverter.ConvertFromString(WritingPresentation.IssueColor(issue.Type));
                run.Background = new SolidColorBrush(Color.FromArgb(0x40, color.R, color.G, color.B));
                run.TextDecorations = TextDecorations.Underline;
                run.ToolTip = $"{WritingPresentation.IssueLabel(issue.Type)}: {WritingPresentation.IssueText(issue)}";
            }

            AnswerHighlightText.Inlines.Add(run);
        }

        IssuesPanel.Children.Clear();
        NoIssuesText.Visibility = feedback.Issues.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        foreach (var issue in feedback.Issues)
            IssuesPanel.Children.Add(CreateIssueRow(issue));

        CorrectedText.Text = feedback.CorrectedSentence;
        ResultCallText.Text = WritingPresentation.CallText(feedback.Call);
        NaturalSection.Visibility = string.IsNullOrWhiteSpace(feedback.NaturalAlternative) ? Visibility.Collapsed : Visibility.Visible;
        NaturalText.Text = feedback.NaturalAlternative ?? string.Empty;

        // Point to the lesson when this check repeats a grammar mistake the learner keeps making.
        var repeated = _history.RepeatedTopics(feedback.Issues).FirstOrDefault(stat => stat.Topic.Key != _drill?.Topic.Key);
        GrammarHintButton.Visibility = repeated is null ? Visibility.Collapsed : Visibility.Visible;
        GrammarHintButton.Tag = repeated?.Topic.Key;
        GrammarHintText.Text = repeated is null ? string.Empty : GrammarPresentation.RepeatHint(repeated);
    }

    private UIElement CreateIssueRow(WritingIssue issue)
    {
        var color = BrushFromHex(WritingPresentation.IssueColor(issue.Type));
        var row = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var badge = new Border
        {
            BorderBrush = color,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(8, 1, 8, 1),
            Margin = new Thickness(0, 1, 10, 0),
            HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Child = new TextBlock { Text = WritingPresentation.IssueLabel(issue.Type), FontSize = 11, Foreground = color }
        };
        row.Children.Add(badge);

        var details = new StackPanel();
        details.Children.Add(new TextBlock
        {
            Text = WritingPresentation.IssueText(issue),
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("TextPrimaryBrush"),
            TextWrapping = TextWrapping.Wrap
        });
        if (!string.IsNullOrWhiteSpace(issue.Explanation))
        {
            details.Children.Add(new TextBlock
            {
                Text = issue.Explanation,
                FontSize = 12.5,
                Foreground = (Brush)FindResource("TextMutedBrush"),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 2, 0, 0)
            });
        }

        Grid.SetColumn(details, 1);
        row.Children.Add(details);
        return row;
    }

    private static Brush BrushFromHex(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }

    // ---- Corrected sentence: audio and save ---------------------------------------------------

    private async void AudioButton_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        var text = EnglishResult();
        if (!string.IsNullOrWhiteSpace(text))
            await PlayAsync(text);
    }

    private void SaveToListButton_Click(object sender, RoutedEventArgs e)
    {
        // Study lists hold English sentences: in EN → VI practice the English source is saved.
        var corrected = EnglishResult();
        if (_draft is null || string.IsNullOrWhiteSpace(corrected))
            return;

        if (SaveListSelector.SelectedItem is not ComboBoxItem { Tag: Guid listId } item)
        {
            ShowSaveStatus("Choose a study list.", isError: true);
            return;
        }

        var result = _draft.AddSentenceToList(listId, corrected);
        if (!result.IsValid)
        {
            ShowSaveStatus(result.Error ?? "Could not add the sentence.", isError: true);
            return;
        }

        ShowSaveStatus($"Added to \"{item.Content}\". Click Save changes to keep it.", isError: false);
        _onDraftChanged?.Invoke();
    }

    private void ShowSaveStatus(string text, bool isError)
    {
        SaveStatusText.Text = text;
        SaveStatusText.Foreground = isError
            ? new SolidColorBrush(Color.FromRgb(0xFC, 0xA5, 0xA5))
            : new SolidColorBrush(Color.FromRgb(0x34, 0xD3, 0x99));
        SaveStatusText.Visibility = Visibility.Visible;
    }

    private void Audio_StateChanged(AudioPlaybackState state)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.InvokeAsync(() => Audio_StateChanged(state));
            return;
        }

        if (_disposed)
            return;

        var active = state.IsLoading || state.IsPlaying;
        AudioButton.Content = active ? "" : "";
        AudioButton.Foreground = active ? (Brush)FindResource("AccentBrush") : (Brush)FindResource("TextMutedBrush");
        AudioButton.ToolTip = state.IsLoading ? "Loading audio..." : active ? "Stop audio" : state.Error ?? "Play audio (TTS)";
    }

    private void WritingPage_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (!IsVisible)
            _audio.Stop();
        else
            RefreshFromDraft();
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        PreviewKeyDown -= WritingPage_PreviewKeyDown;
        _history.Changed -= History_Changed;
        IsVisibleChanged -= WritingPage_IsVisibleChanged;
        if (_session is not null)
        {
            _session.StateChanged -= Session_StateChanged;
            _session.Dispose();
            _session = null;
        }

        _audio.StateChanged -= Audio_StateChanged;
        _audio.Dispose();
    }
}
