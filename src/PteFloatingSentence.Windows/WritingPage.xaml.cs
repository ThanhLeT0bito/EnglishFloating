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
    private readonly ISentenceAudioPlayback _audio;
    private StudyListDraft? _draft;
    private WritingSession? _session;
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
    }

    /// <summary>The practice flow, or null when no AI coach is available.</summary>
    public WritingSession? Session => _session;

    public void Initialize(StudyListDraft draft, IWritingCoach? coach, Action? onDraftChanged = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _draft = draft ?? throw new ArgumentNullException(nameof(draft));
        _onDraftChanged = onDraftChanged;
        if (_session is not null)
        {
            _session.StateChanged -= Session_StateChanged;
            _session.Dispose();
            _session = null;
        }

        if (coach is not null)
        {
            _session = new WritingSession(coach, prefetchNext: true);
            _session.StateChanged += Session_StateChanged;
        }

        RefreshFromDraft();
        RenderSession();
    }

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
            settings.WritingExplanationLanguage);
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

    private async void CheckButton_Click(object sender, RoutedEventArgs e) => await CheckAsync();

    private async void RetryButton_Click(object sender, RoutedEventArgs e)
    {
        if (_session is not null)
            await _session.RetryAsync();
    }

    private void TipButton_Click(object sender, RoutedEventArgs e) => _session?.RevealNextTip();

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
        await _session.GenerateAsync(topics, settings.WritingLevel, settings.WritingExplanationLanguage);
        if (_session?.State == WritingSessionState.Answering)
        {
            // Options are set; fold them away so the sentence and answer get the space.
            _setupCollapsed = true;
            RenderSetupCard();
            AnswerBox.Focus();
        }
    }

    /// <summary>Checks the text in the answer box.</summary>
    public async Task CheckAsync()
    {
        if (_disposed || _session is null || _draft is null)
            return;

        _audio.Stop();
        await _session.CheckAsync(AnswerBox.Text, _draft.Settings.WritingExplanationLanguage);
    }

    private void WritingPage_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var ctrl = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;
        var shift = (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;

        if (ctrl && e.Key == Key.H)
        {
            e.Handled = true;
            _session?.RevealNextTip();
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

        if (WritingPresentation.EnterMeansNext(_session.State, _session.LastAnswer, AnswerBox.Text))
            await GenerateAsync();
        else
            await CheckAsync();
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
        AnswerSection.Visibility = hasPrompt ? Visibility.Visible : Visibility.Collapsed;
        AnswerBox.IsReadOnly = session.State == WritingSessionState.Checking;
        CheckButton.IsEnabled = hasPrompt && session.State is WritingSessionState.Answering or WritingSessionState.Reviewed;
        CheckButton.Content = session.State == WritingSessionState.Reviewed ? "Check again" : "Check";

        RenderFeedback(session);
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
        VietnameseText.Text = prompt.Vietnamese;
        PromptCallText.Text = WritingPresentation.CallText(prompt.Call, session.PromptWasPrefetched);

        var available = session.AvailableTipTiers;
        TipButtonText.Text = WritingPresentation.TipButtonText(session.TipsRevealed, available.Count);
        TipButton.IsEnabled = session.CanRevealTip;

        var revealed = session.RevealedTipTiers;
        TipsPanel.Visibility = revealed.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        VocabularyTipSection.Visibility = revealed.Contains(WritingTipTier.Vocabulary) ? Visibility.Visible : Visibility.Collapsed;
        VocabularyTipList.Children.Clear();
        foreach (var hint in prompt.Tips.Vocabulary)
        {
            VocabularyTipList.Children.Add(new TextBlock
            {
                Text = "• " + WritingPresentation.VocabularyLine(hint),
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
        var text = _session?.Feedback?.CorrectedSentence;
        if (string.IsNullOrWhiteSpace(text) || _draft is null)
            return;

        var settings = _draft.Settings;
        var voice = string.IsNullOrWhiteSpace(settings.TtsVoice) ? EdgeNeuralTtsService.DefaultVoice : settings.TtsVoice;
        var speed = settings.TtsSpeed is >= 0.8 and <= 1.2 ? settings.TtsSpeed : 1.0;
        await _audio.ToggleAsync(text, voice, speed);
    }

    private void SaveToListButton_Click(object sender, RoutedEventArgs e)
    {
        var corrected = _session?.Feedback?.CorrectedSentence;
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
