using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using PteFloatingSentence.Core;
using PteFloatingSentence.Windows.Infrastructure;
using Brush = System.Windows.Media.Brush;
using Button = System.Windows.Controls.Button;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using Orientation = System.Windows.Controls.Orientation;
using UserControl = System.Windows.Controls.UserControl;
using ValidationResult = PteFloatingSentence.Core.ValidationResult;

namespace PteFloatingSentence.Windows;

/// <summary>Audio the writing panel asks the host to play. <see cref="Restart"/> plays from the start instead of toggling.</summary>
public sealed record WritingAudioRequest(string Text, bool Restart = false, bool Slow = false);

/// <summary>
/// Compact Vietnamese → English writing practice for the floating window: the input bar sits on top, the
/// Vietnamese sentence below it, then two sections that fold under an arrow: "Topics &amp; level" and "Result".
/// Preference changes are reported through <see cref="PreferencesChanged"/> so the app can save them.
/// </summary>
public partial class FloatingWritingPanel : UserControl, IDisposable
{
    private AppSettings _settings = AppSettings.Default;
    private WritingSession? _session;
    private bool _rendering;
    private bool _disposed;
    private bool _topicsCollapsed = true;
    private bool _resultCollapsed;
    private WritingHistoryBook? _history;

    public FloatingWritingPanel()
    {
        InitializeComponent();
        PreviewKeyDown += Panel_PreviewKeyDown;
        RenderPreferences();
        RenderSession();
    }

    /// <summary>Raised with updated settings after the learner changes topics, level or explanation language.</summary>
    public event Action<AppSettings>? PreferencesChanged;

    public event Action? ExitRequested;

    /// <summary>Asks the host to play (or stop) audio: the corrected sentence, or the dictation sentence.</summary>
    public event Action<WritingAudioRequest>? AudioRequested;

    /// <summary>Adds the corrected sentence to the active study list; supplied by the host.</summary>
    public Func<string, ValidationResult>? SaveSentence { get; set; }

    public WritingSession? Session => _session;
    public bool IsTopicsCollapsed => TopicsBody.Visibility != Visibility.Visible;
    public bool IsResultCollapsed => ResultBody.Visibility != Visibility.Visible;

    public void SetCoach(IWritingCoach? coach)
    {
        if (_disposed)
            return;

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
            ApplySessionOptions();
        }

        RenderSession();
    }

    /// <summary>Connects the shared mistake notebook; checks are recorded there and weak points come from it.</summary>
    public void SetHistory(WritingHistoryBook? history)
    {
        if (_disposed)
            return;

        if (_history is not null)
            _history.Changed -= History_Changed;
        _history = history;
        if (_history is not null)
            _history.Changed += History_Changed;
        if (_session is not null)
            _session.History = _history;
        RenderWeakPoints();
    }

    private void History_Changed()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.InvokeAsync(History_Changed);
            return;
        }

        if (!_disposed)
            RenderWeakPoints();
    }

    private void ApplySessionOptions()
    {
        if (_session is null)
            return;

        _session.Mode = _settings.WritingPracticeMode;
        _session.RetypeEnabled = _settings.WritingRetypeEnabled;
    }

    /// <summary>Weak points to target with the next sentence, or none when the switch is off or not available yet.</summary>
    private IReadOnlyList<WritingSkillTag> FocusTags() =>
        _settings.WritingFocusWeakPoints && _history is { CanPracticeWeakPoints: true } ? _history.WeakTags() : [];

    /// <summary>Takes topics, level and language from the app's current settings.</summary>
    public void UpdateSettings(AppSettings settings)
    {
        if (_disposed)
            return;

        _settings = WritingTopicRules.Normalize(settings ?? AppSettings.Default);
        ApplySessionOptions();
        RenderPreferences();
        RenderSession();
    }

    /// <summary>Shows the panel's current exercise, generating the first sentence when there is none yet.</summary>
    public async Task ActivateAsync(AppSettings settings)
    {
        UpdateSettings(settings);
        if (_session is { Prompt: null, IsBusy: false })
            await GenerateAsync();
        else
            FocusInput();
    }

    /// <summary>Stops in-flight work when the floating window leaves writing mode.</summary>
    public void Deactivate() => _session?.Cancel();

    // ---- Preferences --------------------------------------------------------------------------

    private void RenderPreferences()
    {
        _rendering = true;
        try
        {
            RenderTopics();
            EasyLevelButton.IsChecked = _settings.WritingLevel == WritingLevel.Easy;
            MediumLevelButton.IsChecked = _settings.WritingLevel == WritingLevel.Medium;
            HardLevelButton.IsChecked = _settings.WritingLevel == WritingLevel.Hard;
            EnglishExplanationButton.IsChecked = _settings.WritingExplanationLanguage == WritingExplanationLanguage.English;
            VietnameseExplanationButton.IsChecked = _settings.WritingExplanationLanguage == WritingExplanationLanguage.Vietnamese;
            TranslateModeButton.IsChecked = _settings.WritingPracticeMode == WritingPracticeMode.Translate;
            DictationModeButton.IsChecked = _settings.WritingPracticeMode == WritingPracticeMode.Dictation;
            ParaphraseModeButton.IsChecked = _settings.WritingPracticeMode == WritingPracticeMode.Paraphrase;
            CombineModeButton.IsChecked = _settings.WritingPracticeMode == WritingPracticeMode.Combine;
            ReverseModeButton.IsChecked = _settings.WritingPracticeMode == WritingPracticeMode.Reverse;
            RetypeCheckBox.IsChecked = _settings.WritingRetypeEnabled;
            AutoPlayCheckBox.IsChecked = _settings.WritingAutoPlayCorrected;
            RenderWeakPoints();
            RenderSections();
        }
        finally
        {
            _rendering = false;
        }
    }

    private void RenderTopics()
    {
        TopicPanel.Children.Clear();
        var selected = _settings.SelectedWritingTopicKeys.ToHashSet(StringComparer.Ordinal);
        foreach (var topic in WritingTopicRules.AllTopics(_settings))
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
                chip.Margin = new Thickness(0, 0, 6, 6);
                TopicPanel.Children.Add(chip);
                continue;
            }

            var remove = new Button { Tag = topic.Key, Style = (Style)FindResource("ChipRemoveButtonStyle") };
            remove.Click += RemoveTopicButton_Click;
            var group = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 6, 6) };
            group.Children.Add(chip);
            group.Children.Add(remove);
            TopicPanel.Children.Add(group);
        }
    }

    private void ChangePreferences(AppSettings updated)
    {
        _settings = WritingTopicRules.Normalize(updated);
        ApplySessionOptions();
        PreferencesChanged?.Invoke(_settings);
    }

    private void RenderWeakPoints()
    {
        var available = _history is { CanPracticeWeakPoints: true };
        var wasRendering = _rendering;
        _rendering = true;
        try
        {
            WeakPointsCheckBox.IsEnabled = available;
            WeakPointsCheckBox.IsChecked = available && _settings.WritingFocusWeakPoints;
            WeakPointsText.Text = WritingPresentation.WeakPointsText(
                available,
                _history?.WeakTags() ?? [],
                _history?.RecentMistakeCount ?? 0);
        }
        finally
        {
            _rendering = wasRendering;
        }
    }

    private void ModeButton_Checked(object sender, RoutedEventArgs e)
    {
        if (_rendering)
            return;

        var mode = ModeFor(sender);
        if (mode == _settings.WritingPracticeMode)
            return;

        ChangePreferences(_settings with { WritingPracticeMode = mode });
        RenderSession();
        if (_session is null)
            return;

        // A paraphrase or combine exercise needs its own kind of sentence: get one right away.
        if (_session.PromptNeedsNewMode || (_session.State == WritingSessionState.Generating && _session.Prompt is null))
            _ = GenerateAsync();
        else if (mode == WritingPracticeMode.Dictation && _session is { State: WritingSessionState.Answering, Prompt: not null } session)
            RequestAudio(session.Prompt.ReferenceEnglish, restart: true);
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
        if (_rendering)
            return;

        ChangePreferences(_settings with
        {
            WritingRetypeEnabled = RetypeCheckBox.IsChecked == true,
            WritingAutoPlayCorrected = AutoPlayCheckBox.IsChecked == true,
            WritingFocusWeakPoints = WeakPointsCheckBox.IsEnabled ? WeakPointsCheckBox.IsChecked == true : _settings.WritingFocusWeakPoints
        });
        RenderSections();
    }

    private void TopicChip_Click(object sender, RoutedEventArgs e)
    {
        if (_rendering || sender is not ToggleButton { Tag: string key } chip)
            return;

        var isSelected = _settings.SelectedWritingTopicKeys.Contains(key, StringComparer.Ordinal);
        if (isSelected != (chip.IsChecked == true))
            ChangePreferences(WritingTopicRules.ToggleSelection(_settings, key));
        RenderSections();
    }

    private void RemoveTopicButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string key })
            return;

        ChangePreferences(WritingTopicRules.RemoveCustom(_settings, key));
        TopicErrorText.Text = string.Empty;
        RenderPreferences();
    }

    private void AddTopicButton_Click(object sender, RoutedEventArgs e) => AddTopic();

    private void AddTopic()
    {
        var keysBefore = _settings.CustomWritingTopics.Select(topic => topic.Key).ToHashSet(StringComparer.Ordinal);
        var (updated, result) = WritingTopicRules.AddCustom(_settings, NewTopicInput.Text);
        if (!result.IsValid)
        {
            TopicErrorText.Text = result.Error ?? string.Empty;
            return;
        }

        var added = updated.CustomWritingTopics.FirstOrDefault(topic => !keysBefore.Contains(topic.Key));
        if (added is not null)
            updated = WritingTopicRules.ToggleSelection(updated, added.Key);

        ChangePreferences(updated);
        NewTopicInput.Text = string.Empty;
        TopicErrorText.Text = string.Empty;
        RenderPreferences();
    }

    private void LevelButton_Checked(object sender, RoutedEventArgs e)
    {
        if (_rendering)
            return;

        var level = ReferenceEquals(sender, EasyLevelButton) ? WritingLevel.Easy
            : ReferenceEquals(sender, HardLevelButton) ? WritingLevel.Hard
            : WritingLevel.Medium;
        ChangePreferences(_settings with { WritingLevel = level });
        RenderSections();
    }

    private void LanguageButton_Checked(object sender, RoutedEventArgs e)
    {
        if (_rendering)
            return;

        ChangePreferences(_settings with
        {
            WritingExplanationLanguage = ReferenceEquals(sender, VietnameseExplanationButton)
                ? WritingExplanationLanguage.Vietnamese
                : WritingExplanationLanguage.English
        });
        RenderSections();
    }

    // ---- Sections that fold under an arrow -----------------------------------------------------

    private void TopicsToggleButton_Click(object sender, RoutedEventArgs e)
    {
        _topicsCollapsed = !_topicsCollapsed;
        RenderSections();
    }

    private void ResultToggleButton_Click(object sender, RoutedEventArgs e)
    {
        _resultCollapsed = !_resultCollapsed;
        RenderSections();
    }

    private void RenderSections()
    {
        TopicsBody.Visibility = _topicsCollapsed ? Visibility.Collapsed : Visibility.Visible;
        TopicsChevron.Text = _topicsCollapsed ? "" : "";
        TopicsToggleButton.ToolTip = _topicsCollapsed ? "Show topics and level" : "Hide topics and level";
        TopicsSummaryText.Text = WritingPresentation.SetupSummary(
            WritingTopicRules.SelectedTopics(_settings).Select(topic => topic.Name).ToList(),
            _settings.WritingLevel,
            _settings.WritingExplanationLanguage,
            _settings.WritingPracticeMode,
            _settings.WritingFocusWeakPoints && _history is { CanPracticeWeakPoints: true });

        var hasResult = _session is { State: WritingSessionState.Reviewed, Feedback: not null };
        ResultToggleButton.Visibility = hasResult ? Visibility.Visible : Visibility.Collapsed;
        ResultBody.Visibility = hasResult && !_resultCollapsed ? Visibility.Visible : Visibility.Collapsed;
        ResultChevron.Text = _resultCollapsed ? "" : "";
        ResultToggleButton.ToolTip = _resultCollapsed ? "Show result" : "Hide result";
    }

    // ---- Practice flow -------------------------------------------------------------------------

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
        FocusInput();
    }

    private void ListenButton_Click(object sender, RoutedEventArgs e) => PlayDictation(slow: false);

    private void SlowListenButton_Click(object sender, RoutedEventArgs e) => PlayDictation(slow: true);

    private void PlayDictation(bool slow)
    {
        var reference = _session is { Prompt: { } prompt } session ? WritingModes.ListenText(prompt, session.PromptMode) : null;
        if (!string.IsNullOrWhiteSpace(reference))
            RequestAudio(reference, restart: true, slow: slow);
        FocusInput();
    }

    private void RequestAudio(string text, bool restart = false, bool slow = false)
    {
        if (!string.IsNullOrWhiteSpace(text))
            AudioRequested?.Invoke(new WritingAudioRequest(text, restart, slow));
    }

    /// <summary>Checks the typed retype; a match plays the corrected sentence once more.</summary>
    public void SubmitRetype()
    {
        var session = _session;
        if (_disposed || session is null || !session.IsRetyping)
            return;

        var result = session.SubmitRetype(AnswerInput.Text);
        if (result is { IsMatch: true } && EnglishResult() is { } english)
            RequestAudio(english, restart: true);
        FocusInput();
    }

    private async void NextButton_Click(object sender, RoutedEventArgs e) => await GenerateAsync();

    private void ExitButton_Click(object sender, RoutedEventArgs e) => ExitRequested?.Invoke();

    private void TipButton_Click(object sender, RoutedEventArgs e) => _session?.RevealNextTip();

    private async void RetryButton_Click(object sender, RoutedEventArgs e)
    {
        if (_session is not null)
            await _session.RetryAsync();
    }

    public async Task GenerateAsync()
    {
        if (_disposed)
            return;

        if (_session is null)
        {
            RenderSession();
            return;
        }

        AnswerInput.Text = string.Empty;
        SaveStatusText.Visibility = Visibility.Collapsed;
        var topics = WritingTopicRules.SelectedTopics(_settings).Select(topic => topic.Name).ToList();
        ApplySessionOptions();
        await _session.GenerateAsync(topics, _settings.WritingLevel, _settings.WritingExplanationLanguage, FocusTags());
        if (_session is { State: WritingSessionState.Answering } session)
        {
            _topicsCollapsed = true;
            RenderSections();
            FocusInput();
            if (session.Mode == WritingPracticeMode.Dictation && session.Prompt is not null)
                RequestAudio(session.Prompt.ReferenceEnglish, restart: true);
        }
    }

    public async Task CheckAsync()
    {
        if (_disposed || _session is null)
            return;

        ApplySessionOptions();
        await _session.CheckAsync(AnswerInput.Text, _settings.WritingExplanationLanguage);
        if (_session is not { State: WritingSessionState.Reviewed, Feedback: not null } session)
            return;

        if (session.IsRetyping)
        {
            // The answer stays highlighted in the result; the box is now for typing the corrected sentence.
            AnswerInput.Text = string.Empty;
            FocusInput();
        }
        else if (_settings.WritingAutoPlayCorrected && EnglishResult() is { } english)
        {
            RequestAudio(english, restart: true);
        }
    }

    private void FocusInput()
    {
        if (AnswerInput.IsVisible)
        {
            AnswerInput.Focus();
            AnswerInput.CaretIndex = AnswerInput.Text.Length;
        }
    }

    private void AnswerInput_TextChanged(object sender, TextChangedEventArgs e) =>
        AnswerPlaceholder.Visibility = string.IsNullOrEmpty(AnswerInput.Text) ? Visibility.Visible : Visibility.Collapsed;

    private void Panel_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var ctrl = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;
        if (ctrl && e.Key == Key.H)
        {
            e.Handled = true;
            if (_session is not null && _session.PromptMode != WritingPracticeMode.Dictation)
                _session.RevealNextTip();
            return;
        }

        if (ctrl && e.Key == Key.Space && _session is { Prompt: not null } && WritingPresentation.ShowsListenBar(_session.PromptMode))
        {
            e.Handled = true;
            PlayDictation(slow: (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift);
            return;
        }

        if (e.Key != Key.Enter && e.Key != Key.Return)
            return;

        if (ReferenceEquals(e.OriginalSource, NewTopicInput))
        {
            e.Handled = true;
            AddTopic();
            return;
        }

        if (ReferenceEquals(e.OriginalSource, AnswerInput))
        {
            e.Handled = true;
            _ = HandleAnswerEnterAsync();
        }
    }

    private async Task HandleAnswerEnterAsync()
    {
        if (_session is null || _session.IsBusy)
            return;

        switch (_session.EnterActionFor(AnswerInput.Text))
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
        if (session is null)
        {
            ShowStatus(WritingPresentation.NoCoachMessage, isError: true, canRetry: false);
            VietnameseText.Text = string.Empty;
            PromptMetaText.Text = string.Empty;
            PromptCallText.Text = string.Empty;
            TipsPanel.Visibility = Visibility.Collapsed;
            DictationBar.Visibility = Visibility.Collapsed;
            RetypePanel.Visibility = Visibility.Collapsed;
            CheckButton.IsEnabled = TipButton.IsEnabled = NextButton.IsEnabled = false;
            RenderSections();
            return;
        }

        var busy = WritingPresentation.BusyText(session.State);
        if (busy is not null)
            ShowStatus(busy, isError: false, canRetry: false);
        else if (!string.IsNullOrEmpty(session.Error))
            ShowStatus(session.Error, isError: true, canRetry: session.CanRetry);
        else
            StatusPanel.Visibility = Visibility.Collapsed;

        var prompt = session.Prompt;
        var mode = session.PromptMode;
        var dictation = mode == WritingPracticeMode.Dictation;
        var reviewed = session.State == WritingSessionState.Reviewed;
        // In dictation the Vietnamese meaning stays hidden until the answer is checked.
        var hideMeaning = dictation && !reviewed;
        VietnameseText.Text = prompt is null ? string.Empty : WritingModes.PromptText(prompt, mode);
        VietnameseText.Visibility = hideMeaning ? Visibility.Collapsed : Visibility.Visible;
        DictationBar.Visibility = prompt is not null && WritingPresentation.ShowsListenBar(mode) ? Visibility.Visible : Visibility.Collapsed;
        DictationHintText.Visibility = hideMeaning ? Visibility.Visible : Visibility.Collapsed;
        DictationHintText.Text = WritingPresentation.DictationPromptText;
        var instruction = prompt is null ? string.Empty : WritingPresentation.InstructionText(prompt, mode);
        InstructionText.Text = instruction;
        InstructionText.Visibility = instruction.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        var meaning = prompt is not null && reviewed ? WritingPresentation.MeaningLine(prompt, mode) : string.Empty;
        MeaningText.Text = meaning;
        MeaningText.Visibility = meaning.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        PromptMetaText.Text = prompt is null ? string.Empty : WritingPresentation.PromptMeta(prompt);
        PromptCallText.Text = prompt is null ? string.Empty : WritingPresentation.CallText(prompt.Call, session.PromptWasPrefetched);
        AnswerInput.IsReadOnly = session.State == WritingSessionState.Checking;
        AnswerPlaceholder.Text = WritingPresentation.AnswerPlaceholder(mode, session.IsRetyping);
        CheckButton.Content = WritingPresentation.CheckButtonText(session.State, session.IsRetyping);
        CheckButton.IsEnabled = prompt is not null && session.State is WritingSessionState.Answering or WritingSessionState.Reviewed;
        NextButton.IsEnabled = true;
        TipButton.Visibility = dictation ? Visibility.Collapsed : Visibility.Visible;
        TipButton.IsEnabled = session.CanRevealTip;
        TipButtonText.Text = WritingPresentation.TipButtonText(session.TipsRevealed, session.AvailableTipTiers.Count).Replace("Tip ", string.Empty);
        RenderRetype(session);

        RenderTips(session);
        RenderFeedback(session);
        RenderSections();
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

    private void ShowStatus(string text, bool isError, bool canRetry)
    {
        StatusText.Text = text;
        StatusText.Foreground = isError
            ? new SolidColorBrush(Color.FromRgb(0xFC, 0xA5, 0xA5))
            : (Brush)FindResource("TextMutedBrush");
        RetryButton.Visibility = canRetry ? Visibility.Visible : Visibility.Collapsed;
        StatusPanel.Visibility = Visibility.Visible;
    }

    private void RenderTips(WritingSession session)
    {
        var prompt = session.Prompt;
        var revealed = session.RevealedTipTiers;
        TipsPanel.Visibility = prompt is not null && revealed.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (prompt is null)
            return;

        VocabularyTipSection.Visibility = revealed.Contains(WritingTipTier.Vocabulary) ? Visibility.Visible : Visibility.Collapsed;
        VocabularyTipList.Children.Clear();
        foreach (var hint in prompt.Tips.Vocabulary)
        {
            VocabularyTipList.Children.Add(new TextBlock
            {
                Text = "• " + WritingPresentation.VocabularyLine(hint, session.PromptMode),
                FontSize = 12.5,
                Foreground = (Brush)FindResource("TextSecondaryBrush"),
                TextWrapping = TextWrapping.Wrap
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
            return;

        var verdictBrush = BrushFromHex(WritingPresentation.VerdictColor(feedback.Verdict));
        var tipsUsed = WritingPresentation.TipsUsedText(session.TipsUsedForFeedback);
        ResultSummaryText.Text = $"{feedback.Score} · {WritingPresentation.VerdictLabel(feedback.Verdict)}"
            + (tipsUsed.Length > 0 ? $" · {tipsUsed}" : string.Empty);
        ResultSummaryText.Foreground = verdictBrush;
        SummaryText.Text = feedback.Summary;
        SummaryText.Visibility = string.IsNullOrWhiteSpace(feedback.Summary) ? Visibility.Collapsed : Visibility.Visible;

        var answer = session.LastAnswer ?? string.Empty;
        AnswerHighlightText.Inlines.Clear();
        foreach (var segment in WritingPresentation.SplitAnswer(answer, WritingFeedbackRules.MapHighlights(answer, feedback.Issues)))
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
        NaturalText.Text = string.IsNullOrWhiteSpace(feedback.NaturalAlternative) ? string.Empty : "Natural: " + feedback.NaturalAlternative;
        NaturalText.Visibility = string.IsNullOrWhiteSpace(feedback.NaturalAlternative) ? Visibility.Collapsed : Visibility.Visible;
        ResultCallText.Text = WritingPresentation.CallText(feedback.Call);
    }

    private UIElement CreateIssueRow(WritingIssue issue)
    {
        var color = BrushFromHex(WritingPresentation.IssueColor(issue.Type));
        var row = new Grid { Margin = new Thickness(0, 0, 0, 6) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.Children.Add(new Border
        {
            BorderBrush = color,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(7, 0, 7, 1),
            Margin = new Thickness(0, 1, 8, 0),
            HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Child = new TextBlock { Text = WritingPresentation.IssueLabel(issue.Type), FontSize = 10.5, Foreground = color }
        });

        var details = new StackPanel();
        details.Children.Add(new TextBlock
        {
            Text = WritingPresentation.IssueText(issue),
            FontSize = 12.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("TextPrimaryBrush"),
            TextWrapping = TextWrapping.Wrap
        });
        if (!string.IsNullOrWhiteSpace(issue.Explanation))
        {
            details.Children.Add(new TextBlock
            {
                Text = issue.Explanation,
                FontSize = 12,
                Foreground = (Brush)FindResource("TextMutedBrush"),
                TextWrapping = TextWrapping.Wrap
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

    // ---- Corrected sentence: audio and save ----------------------------------------------------

    private void AudioButton_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        var text = EnglishResult();
        if (!string.IsNullOrWhiteSpace(text))
            RequestAudio(text);
    }

    private void SaveToListButton_Click(object sender, RoutedEventArgs e)
    {
        // Study lists hold English sentences: in EN → VI practice the English source is saved.
        var corrected = EnglishResult();
        if (string.IsNullOrWhiteSpace(corrected))
            return;

        var result = SaveSentence?.Invoke(corrected) ?? new ValidationResult(false, "Saving is not available.");
        SaveStatusText.Text = result.IsValid ? "Saved to your active study list." : result.Error ?? "Could not save the sentence.";
        SaveStatusText.Foreground = result.IsValid
            ? new SolidColorBrush(Color.FromRgb(0x34, 0xD3, 0x99))
            : new SolidColorBrush(Color.FromRgb(0xFC, 0xA5, 0xA5));
        SaveStatusText.Margin = new Thickness(10, 0, 0, 0);
        SaveStatusText.Visibility = Visibility.Visible;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        PreviewKeyDown -= Panel_PreviewKeyDown;
        if (_history is not null)
            _history.Changed -= History_Changed;
        if (_session is not null)
        {
            _session.StateChanged -= Session_StateChanged;
            _session.Dispose();
            _session = null;
        }
    }
}
