using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using PteFloatingSentence.Core;
using PteFloatingSentence.Windows.Infrastructure;
using Brush = System.Windows.Media.Brush;
using Button = System.Windows.Controls.Button;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using Orientation = System.Windows.Controls.Orientation;
using UserControl = System.Windows.Controls.UserControl;

namespace PteFloatingSentence.Windows;

/// <summary>
/// Settings page for learning grammar: search any topic, see the grammar the learner keeps getting wrong, and open a
/// lesson that explains the essence first, then form, uses, mistakes, examples and a quick check, with a button to
/// practice the topic on the Writing page.
/// </summary>
public partial class GrammarPage : UserControl, IDisposable
{
    public const int SearchResultLimit = 8;
    public const int OwnMistakeRows = 5;

    private readonly ISentenceAudioPlayback _audio;
    private readonly Dictionary<int, int> _quizAnswers = [];
    private StudyListDraft? _draft;
    private GrammarLessonLibrary _library = GrammarLessonLibrary.InMemory();
    private WritingHistoryBook _history = WritingHistoryBook.InMemory();
    private GrammarTopic? _topic;
    private GrammarLesson? _lesson;
    private CancellationTokenSource? _loading;
    private long _loadGeneration;
    private bool _disposed;

    public GrammarPage() : this(new SentenceAudioPlayback(new WpfAudioPlayer(), new EdgeNeuralTtsService(), new AudioCacheManager()))
    {
    }

    public GrammarPage(ISentenceAudioPlayback audio)
    {
        _audio = audio ?? throw new ArgumentNullException(nameof(audio));
        InitializeComponent();
        SearchInput.PreviewKeyDown += SearchInput_PreviewKeyDown;
        IsVisibleChanged += GrammarPage_IsVisibleChanged;
        RenderBrowse();
    }

    /// <summary>Raised when the learner wants to practice <see cref="GrammarTopic"/> on the Writing page.</summary>
    public event Action<GrammarTopic>? PracticeRequested;

    /// <summary>Raised when every quick-check question of the open lesson is answered: topic, correct answers, questions.</summary>
    public event Action<GrammarTopic, int, int>? QuizCompleted;

    public GrammarTopic? CurrentTopic => _topic;
    public GrammarLesson? CurrentLesson => _lesson;
    public bool IsLessonOpen => LessonView.Visibility == Visibility.Visible;

    private WritingExplanationLanguage ExplanationLanguage => _draft?.Settings.WritingExplanationLanguage ?? WritingExplanationLanguage.English;

    public void Initialize(StudyListDraft draft, GrammarLessonLibrary? library, WritingHistoryBook? history)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _draft = draft ?? throw new ArgumentNullException(nameof(draft));
        _library = library ?? GrammarLessonLibrary.InMemory();
        _history.Changed -= History_Changed;
        _history = history ?? WritingHistoryBook.InMemory();
        _history.Changed += History_Changed;
        RenderBrowse();
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

        RenderBrowse();
        if (_topic is not null)
        {
            RenderLessonHeader(_topic);
            if (_lesson is not null)
                RenderOwnMistakes(_topic);
        }
    }

    // ---- Browse ---------------------------------------------------------------------------------

    /// <summary>Shows the search, the weak topics and every topic.</summary>
    public void ShowBrowse()
    {
        CancelLoading();
        _audio.Stop();
        BrowseView.Visibility = Visibility.Visible;
        LessonView.Visibility = Visibility.Collapsed;
        RenderBrowse();
    }

    private void RenderBrowse()
    {
        if (_disposed)
            return;

        WeakTopicsPanel.Children.Clear();
        var recommendations = _history.GrammarRecommendations();
        foreach (var stat in recommendations)
            WeakTopicsPanel.Children.Add(CreateWeakTopicRow(stat));
        NoWeakTopicsText.Visibility = recommendations.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        CategoriesPanel.Children.Clear();
        foreach (var group in GrammarTopics.All.GroupBy(topic => topic.Category))
        {
            CategoriesPanel.Children.Add(new TextBlock
            {
                Text = GrammarTopics.CategoryLabel(group.Key),
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)FindResource("TextMutedBrush"),
                Margin = new Thickness(2, 8, 0, 6)
            });
            var wrap = new WrapPanel();
            foreach (var topic in group)
                wrap.Children.Add(CreateTopicButton(topic));
            CategoriesPanel.Children.Add(wrap);
        }

        RenderSearchResults();
    }

    private UIElement CreateWeakTopicRow(GrammarTopicStat stat)
    {
        var row = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var text = new StackPanel();
        var title = new StackPanel { Orientation = Orientation.Horizontal };
        title.Children.Add(new TextBlock
        {
            Text = GrammarPresentation.TopicTitle(stat.Topic),
            FontSize = 13.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("TextPrimaryBrush"),
            VerticalAlignment = VerticalAlignment.Center
        });
        title.Children.Add(CreateStatusChip(stat.Status, new Thickness(10, 0, 0, 0)));
        text.Children.Add(title);
        text.Children.Add(new TextBlock
        {
            Text = GrammarPresentation.StatLine(stat),
            FontSize = 12,
            Foreground = (Brush)FindResource("TextMutedBrush"),
            Margin = new Thickness(0, 2, 0, 0)
        });
        var example = _history.MistakesFor(stat.Topic.Key, 1).FirstOrDefault();
        if (example is not null)
        {
            text.Children.Add(new TextBlock
            {
                Text = WritingPresentation.MistakeGroupText(example),
                FontSize = 12,
                FontStyle = FontStyles.Italic,
                Foreground = (Brush)FindResource("TextSecondaryBrush"),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 2, 0, 0)
            });
        }
        row.Children.Add(text);

        var learn = new Button
        {
            Content = GrammarPresentation.LearnButtonText(stat.Topic),
            Tag = stat.Topic.Key,
            Style = (Style)FindResource("PrimaryButtonStyle"),
            Margin = new Thickness(12, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        learn.Click += TopicButton_Click;
        Grid.SetColumn(learn, 1);
        row.Children.Add(learn);
        return row;
    }

    private Button CreateTopicButton(GrammarTopic topic)
    {
        var status = _history.StatFor(topic).Status;
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        if (status != GrammarTopicStatus.NotStarted)
        {
            content.Children.Add(new Ellipse
            {
                Width = 7,
                Height = 7,
                Fill = BrushFromHex(GrammarPresentation.StatusColor(status)),
                Margin = new Thickness(0, 0, 7, 0),
                VerticalAlignment = VerticalAlignment.Center
            });
        }
        content.Children.Add(new TextBlock { Text = topic.Name, VerticalAlignment = VerticalAlignment.Center });

        var button = new Button
        {
            Content = content,
            Tag = topic.Key,
            ToolTip = status == GrammarTopicStatus.NotStarted ? topic.VietnameseName : $"{topic.VietnameseName} · {GrammarPresentation.StatusLabel(status)}",
            Style = (Style)FindResource("TopicButtonStyle")
        };
        button.Click += TopicButton_Click;
        return button;
    }

    private async void TopicButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string key } && GrammarTopics.Resolve(key) is { } topic)
            await OpenTopicAsync(topic);
    }

    // ---- Search ---------------------------------------------------------------------------------

    private void SearchInput_TextChanged(object sender, TextChangedEventArgs e)
    {
        SearchPlaceholder.Visibility = string.IsNullOrEmpty(SearchInput.Text) ? Visibility.Visible : Visibility.Collapsed;
        RenderSearchResults();
    }

    private void RenderSearchResults()
    {
        SearchResultsPanel.Children.Clear();
        var query = SearchInput.Text.Trim();
        if (query.Length == 0)
        {
            SearchResultsPanel.Visibility = Visibility.Collapsed;
            SearchHintText.Visibility = Visibility.Collapsed;
            return;
        }

        var matches = GrammarTopics.Search(query).Take(SearchResultLimit).ToList();
        foreach (var topic in matches)
            SearchResultsPanel.Children.Add(CreateTopicButton(topic));
        SearchResultsPanel.Visibility = matches.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        SearchHintText.Text = GrammarPresentation.SearchHint(query, matches.Count);
        SearchHintText.Visibility = Visibility.Visible;
    }

    private void SearchInput_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Enter or Key.Return))
            return;

        // Keep Enter away from the Settings window's default Save button.
        e.Handled = true;
        _ = SearchAsync();
    }

    private async void SearchButton_Click(object sender, RoutedEventArgs e) => await SearchAsync();

    /// <summary>Opens the best match for the search text, or a lesson about it when nothing built in matches.</summary>
    public async Task SearchAsync()
    {
        var query = SearchInput.Text.Trim();
        if (query.Length == 0)
            return;

        var topic = GrammarTopics.Search(query).FirstOrDefault() ?? GrammarTopics.Custom(query);
        if (topic is not null)
            await OpenTopicAsync(topic);
    }

    // ---- Lesson ---------------------------------------------------------------------------------

    /// <summary>Opens a topic by key (built in or custom). Returns false for an unknown key.</summary>
    public async Task<bool> OpenTopicAsync(string key)
    {
        var topic = GrammarTopics.Resolve(key);
        if (topic is null)
            return false;

        await OpenTopicAsync(topic);
        return true;
    }

    /// <summary>Shows the lesson for <paramref name="topic"/>: the saved one at once, otherwise the AI writes it.</summary>
    public async Task OpenTopicAsync(GrammarTopic topic, bool refresh = false)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(topic);
        CancelLoading();
        _audio.Stop();
        _topic = topic;
        _lesson = null;
        _quizAnswers.Clear();
        BrowseView.Visibility = Visibility.Collapsed;
        LessonView.Visibility = Visibility.Visible;
        LessonBody.Visibility = Visibility.Collapsed;
        RenderLessonHeader(topic);
        // The page sits in the Settings scroll view: start the lesson at its title.
        _ = Dispatcher.InvokeAsync(() => BackButton.BringIntoView());

        var language = ExplanationLanguage;
        if (!refresh && _library.Cached(topic, language) is { } cached)
        {
            ShowLesson(cached);
            return;
        }

        var generation = ++_loadGeneration;
        var loading = new CancellationTokenSource();
        _loading = loading;
        ShowLessonStatus(GrammarPresentation.LoadingText, isError: false, canRetry: false);
        RegenerateButton.IsEnabled = false;
        try
        {
            var lesson = await _library.GetAsync(topic, language, refresh, loading.Token);
            if (generation == _loadGeneration && !_disposed)
                ShowLesson(lesson);
        }
        catch (OperationCanceledException) when (loading.IsCancellationRequested)
        {
        }
        catch (Exception error)
        {
            if (generation == _loadGeneration && !_disposed)
            {
                var message = error is InvalidOperationException && !string.IsNullOrWhiteSpace(error.Message) ? error.Message : WritingSession.UnexpectedErrorMessage;
                ShowLessonStatus(message, isError: true, canRetry: true);
            }
        }
        finally
        {
            if (ReferenceEquals(_loading, loading))
                _loading = null;
            loading.Dispose();
            if (generation == _loadGeneration && !_disposed)
                RegenerateButton.IsEnabled = _library.CanWriteLessons;
        }
    }

    private void CancelLoading()
    {
        ++_loadGeneration;
        _loading?.Cancel();
        _loading = null;
    }

    private void RenderLessonHeader(GrammarTopic topic)
    {
        LessonTitleText.Text = GrammarPresentation.TopicTitle(topic);
        var stat = topic.IsCustom ? null : _history.StatFor(topic);
        var status = stat?.Status ?? GrammarTopicStatus.NotStarted;
        StatusChip.BorderBrush = BrushFromHex(GrammarPresentation.StatusColor(status));
        StatusChipText.Foreground = BrushFromHex(GrammarPresentation.StatusColor(status));
        StatusChipText.Text = GrammarPresentation.StatusLabel(status);
        LessonStatLine.Text = stat is { RecentMistakes: > 0 } ? GrammarPresentation.StatLine(stat) : string.Empty;
        LastDrillText.Text = stat is null ? string.Empty : GrammarPresentation.LastDrillText(stat, _history.Now);
        PracticeButton.Content = GrammarPresentation.PracticeButtonText(ExplanationLanguage);
        RegenerateButton.IsEnabled = _library.CanWriteLessons;
    }

    private void ShowLessonStatus(string text, bool isError, bool canRetry)
    {
        LessonStatusText.Text = text;
        LessonStatusText.Foreground = isError ? new SolidColorBrush(Color.FromRgb(0xFC, 0xA5, 0xA5)) : (Brush)FindResource("TextMutedBrush");
        LessonRetryButton.Visibility = canRetry ? Visibility.Visible : Visibility.Collapsed;
        LessonStatusPanel.Visibility = Visibility.Visible;
    }

    private void ShowLesson(GrammarLesson lesson)
    {
        _lesson = lesson;
        _quizAnswers.Clear();
        LessonStatusPanel.Visibility = Visibility.Collapsed;
        LessonBody.Visibility = Visibility.Visible;
        var language = lesson.Language;

        EssenceHeading.Text = GrammarPresentation.EssenceHeading(language);
        EssenceText.Text = lesson.Essence;

        FormHeading.Text = GrammarPresentation.FormHeading(language);
        FormPanel.Children.Clear();
        foreach (var line in lesson.Form)
            FormPanel.Children.Add(CreateFormRow(line));
        FormSection.Visibility = lesson.Form.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        UsesHeading.Text = GrammarPresentation.UsesHeading(language);
        UsesPanel.Children.Clear();
        foreach (var use in lesson.Uses)
            UsesPanel.Children.Add(CreateUseRow(use));
        UsesSection.Visibility = lesson.Uses.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        SignalHeading.Text = GrammarPresentation.SignalHeading(language);
        SignalPanel.Children.Clear();
        foreach (var word in lesson.SignalWords)
            SignalPanel.Children.Add(Chip(word));
        SignalSection.Visibility = lesson.SignalWords.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        ContrastHeading.Text = GrammarPresentation.ContrastHeading(language);
        ContrastText.Text = lesson.Contrast;
        ContrastSection.Visibility = lesson.Contrast.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

        CommonMistakesHeading.Text = GrammarPresentation.CommonMistakesHeading(language);
        CommonMistakesPanel.Children.Clear();
        foreach (var mistake in lesson.CommonMistakes)
            CommonMistakesPanel.Children.Add(CreateMistakeRow(mistake.Wrong, mistake.Right, mistake.Why));
        CommonMistakesSection.Visibility = lesson.CommonMistakes.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        YourMistakesHeading.Text = GrammarPresentation.YourMistakesHeading(language);
        if (_topic is not null)
            RenderOwnMistakes(_topic);

        ExamplesHeading.Text = GrammarPresentation.ExamplesHeading(language);
        ExamplesPanel.Children.Clear();
        foreach (var example in lesson.Examples)
            ExamplesPanel.Children.Add(CreateExampleRow(example));

        QuizHeading.Text = GrammarPresentation.QuizHeading(language);
        RenderQuiz();
        QuizSection.Visibility = lesson.Quiz.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        PracticeButton.Content = GrammarPresentation.PracticeButtonText(language);
        LessonCallText.Text = lesson.Call is null
            ? $"Saved lesson · {WritingPresentation.RelativeDay(lesson.CreatedAt, _history.Now)}"
            : WritingPresentation.CallText(lesson.Call);
    }

    private void RenderOwnMistakes(GrammarTopic topic)
    {
        YourMistakesPanel.Children.Clear();
        var groups = _history.MistakesFor(topic.Key, OwnMistakeRows);
        foreach (var group in groups)
        {
            var mistake = group.Latest;
            var why = string.IsNullOrWhiteSpace(mistake.Explanation)
                ? WritingPresentation.RelativeDay(mistake.At, _history.Now)
                : $"{mistake.Explanation} · {WritingPresentation.RelativeDay(mistake.At, _history.Now)}";
            var wrong = group.Count > 1 ? $"{mistake.Answer} (×{group.Count})" : mistake.Answer;
            YourMistakesPanel.Children.Add(CreateMistakeRow(wrong, mistake.Corrected, why));
        }

        NoYourMistakesText.Text = GrammarPresentation.NoOwnMistakesText(_lesson?.Language ?? ExplanationLanguage);
        NoYourMistakesText.Visibility = groups.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private UIElement CreateFormRow(GrammarFormLine line)
    {
        var row = new Grid { Margin = new Thickness(0, 0, 0, 6) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.Children.Add(new TextBlock
        {
            Text = line.Label,
            FontSize = 12.5,
            Foreground = (Brush)FindResource("TextMutedBrush"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 2, 10, 0)
        });

        var details = new StackPanel();
        details.Children.Add(new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x29, 0x3B)),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10, 4, 10, 4),
            HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
            Child = new TextBlock
            {
                Text = line.Pattern,
                FontFamily = new System.Windows.Media.FontFamily("Consolas"),
                FontSize = 13.5,
                Foreground = new SolidColorBrush(Color.FromRgb(0x93, 0xC5, 0xFD)),
                TextWrapping = TextWrapping.Wrap
            }
        });
        if (line.Example.Length > 0)
            details.Children.Add(ExampleText(line.Example, new Thickness(2, 4, 0, 0)));
        Grid.SetColumn(details, 1);
        row.Children.Add(details);
        return row;
    }

    private UIElement CreateUseRow(GrammarUse use)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
        if (use.Title.Length > 0)
        {
            panel.Children.Add(new TextBlock
            {
                Text = "• " + use.Title,
                FontSize = 13.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)FindResource("TextPrimaryBrush"),
                TextWrapping = TextWrapping.Wrap
            });
        }
        if (use.Explanation.Length > 0)
        {
            panel.Children.Add(new TextBlock
            {
                Text = use.Explanation,
                FontSize = 13,
                Foreground = (Brush)FindResource("TextSecondaryBrush"),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(12, 2, 0, 0)
            });
        }
        if (use.Example.Length > 0)
            panel.Children.Add(ExampleText(use.Example, new Thickness(12, 3, 0, 0)));
        return panel;
    }

    private UIElement CreateMistakeRow(string wrong, string right, string why)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
        panel.Children.Add(new TextBlock
        {
            Text = "✗ " + wrong,
            FontSize = 13.5,
            Foreground = new SolidColorBrush(Color.FromRgb(0xFC, 0xA5, 0xA5)),
            TextWrapping = TextWrapping.Wrap
        });
        panel.Children.Add(new TextBlock
        {
            Text = "✓ " + right,
            FontSize = 13.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(0x34, 0xD3, 0x99)),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 2, 0, 0)
        });
        if (why.Length > 0)
        {
            panel.Children.Add(new TextBlock
            {
                Text = why,
                FontSize = 12.5,
                Foreground = (Brush)FindResource("TextMutedBrush"),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(16, 2, 0, 0)
            });
        }
        return panel;
    }

    private UIElement CreateExampleRow(GrammarExample example)
    {
        var row = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var text = new StackPanel();
        text.Children.Add(new TextBlock
        {
            Text = example.English,
            FontSize = 14,
            Foreground = (Brush)FindResource("TextPrimaryBrush"),
            TextWrapping = TextWrapping.Wrap
        });
        if (example.Vietnamese.Length > 0)
        {
            text.Children.Add(new TextBlock
            {
                Text = example.Vietnamese,
                FontSize = 12.5,
                Foreground = (Brush)FindResource("TextMutedBrush"),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 1, 0, 0)
            });
        }
        row.Children.Add(text);

        var play = new Button { Tag = example.English, Style = (Style)FindResource("AudioButtonStyle"), ToolTip = "Play audio (TTS)", Margin = new Thickness(8, 0, 0, 0) };
        play.Click += PlayButton_Click;
        Grid.SetColumn(play, 1);
        row.Children.Add(play);
        return row;
    }

    private TextBlock ExampleText(string text, Thickness margin) => new()
    {
        Text = text,
        FontSize = 13,
        FontStyle = FontStyles.Italic,
        Foreground = (Brush)FindResource("TextSecondaryBrush"),
        TextWrapping = TextWrapping.Wrap,
        Margin = margin
    };

    private Border Chip(string text) => new()
    {
        Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x29, 0x3B)),
        BorderBrush = (Brush)FindResource("PanelBorderBrush"),
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(12),
        Padding = new Thickness(10, 3, 10, 3),
        Margin = new Thickness(0, 0, 8, 8),
        Child = new TextBlock { Text = text, FontSize = 12.5, Foreground = (Brush)FindResource("TextSecondaryBrush") }
    };

    private Border CreateStatusChip(GrammarTopicStatus status, Thickness margin)
    {
        var color = BrushFromHex(GrammarPresentation.StatusColor(status));
        return new Border
        {
            BorderBrush = color,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(9),
            Padding = new Thickness(7, 0, 7, 1),
            Margin = margin,
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock { Text = GrammarPresentation.StatusLabel(status), FontSize = 11, Foreground = color }
        };
    }

    // ---- Quick check ------------------------------------------------------------------------------

    /// <summary>Number of quiz questions answered for the open lesson.</summary>
    public int QuizAnswered => _quizAnswers.Count;

    private void RenderQuiz()
    {
        QuizPanel.Children.Clear();
        if (_lesson is null)
            return;

        for (var index = 0; index < _lesson.Quiz.Count; index++)
        {
            var item = _lesson.Quiz[index];
            var answered = _quizAnswers.TryGetValue(index, out var chosen);
            var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
            panel.Children.Add(new TextBlock
            {
                Text = $"{index + 1}. {item.Question}",
                FontSize = 13.5,
                Foreground = (Brush)FindResource("TextPrimaryBrush"),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 6)
            });

            var options = new WrapPanel();
            for (var option = 0; option < item.Options.Count; option++)
            {
                var button = new Button
                {
                    Content = item.Options[option],
                    Tag = (index, option),
                    Style = (Style)FindResource("QuizOptionStyle"),
                    IsEnabled = !answered
                };
                if (answered && option == item.AnswerIndex)
                {
                    button.Background = new SolidColorBrush(Color.FromArgb(0x40, 0x34, 0xD3, 0x99));
                    button.BorderBrush = new SolidColorBrush(Color.FromRgb(0x34, 0xD3, 0x99));
                }
                else if (answered && option == chosen)
                {
                    button.Background = new SolidColorBrush(Color.FromArgb(0x40, 0xF8, 0x71, 0x71));
                    button.BorderBrush = new SolidColorBrush(Color.FromRgb(0xF8, 0x71, 0x71));
                }
                button.Click += QuizOption_Click;
                options.Children.Add(button);
            }
            panel.Children.Add(options);

            if (answered)
            {
                var correct = chosen == item.AnswerIndex;
                panel.Children.Add(new TextBlock
                {
                    Text = GrammarPresentation.QuizResult(correct, item.Explanation),
                    FontSize = 12.5,
                    Foreground = correct ? new SolidColorBrush(Color.FromRgb(0x34, 0xD3, 0x99)) : new SolidColorBrush(Color.FromRgb(0xFC, 0xA5, 0xA5)),
                    TextWrapping = TextWrapping.Wrap
                });
            }

            QuizPanel.Children.Add(panel);
        }

        if (_lesson.Quiz.Count == 0 || _quizAnswers.Count < _lesson.Quiz.Count)
            return;

        var (rightAnswers, questions) = QuizScore();
        var passed = new GrammarQuizResult(_history.Now, "quiz", rightAnswers, questions).IsPass;
        var summary = new Border
        {
            Background = passed ? new SolidColorBrush(Color.FromArgb(0x26, 0x34, 0xD3, 0x99)) : new SolidColorBrush(Color.FromArgb(0x26, 0xFB, 0xBF, 0x24)),
            BorderBrush = passed ? new SolidColorBrush(Color.FromArgb(0x80, 0x34, 0xD3, 0x99)) : new SolidColorBrush(Color.FromArgb(0x80, 0xFB, 0xBF, 0x24)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 10, 12, 10)
        };
        var content = new StackPanel();
        content.Children.Add(new TextBlock
        {
            Text = GrammarPresentation.QuizSummary(rightAnswers, questions, ExplanationLanguage),
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("TextPrimaryBrush"),
            TextWrapping = TextWrapping.Wrap
        });
        if (!passed)
        {
            var retry = new Button
            {
                Content = GrammarPresentation.QuizRetryText(ExplanationLanguage),
                Style = (Style)FindResource("SecondaryButtonStyle"),
                HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
                Margin = new Thickness(0, 8, 0, 0)
            };
            retry.Click += (_, _) => RetryQuiz();
            content.Children.Add(retry);
        }

        summary.Child = content;
        QuizPanel.Children.Add(summary);
    }

    /// <summary>Correct answers and questions of the open lesson's quick check.</summary>
    public (int Correct, int Total) QuizScore() =>
        _lesson is null ? (0, 0) : (_quizAnswers.Count(answer => answer.Key < _lesson.Quiz.Count && _lesson.Quiz[answer.Key].AnswerIndex == answer.Value), _lesson.Quiz.Count);

    /// <summary>Answers question <paramref name="index"/> with <paramref name="option"/>; each question is answered once.</summary>
    public void AnswerQuiz(int index, int option)
    {
        if (_lesson is null || _topic is null || index < 0 || index >= _lesson.Quiz.Count || _quizAnswers.ContainsKey(index))
            return;

        _quizAnswers[index] = option;
        RenderQuiz();
        if (_quizAnswers.Count < _lesson.Quiz.Count)
            return;

        var (correct, total) = QuizScore();
        var topic = _topic;
        if (!topic.IsCustom)
            _history.RecordQuiz(topic.Key, correct, total);
        QuizCompleted?.Invoke(topic, correct, total);
    }

    /// <summary>Clears the answers so the quick check can be taken again.</summary>
    public void RetryQuiz()
    {
        _quizAnswers.Clear();
        RenderQuiz();
    }

    private void QuizOption_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: ValueTuple<int, int> choice })
            AnswerQuiz(choice.Item1, choice.Item2);
    }

    // ---- Buttons ----------------------------------------------------------------------------------

    private void BackButton_Click(object sender, RoutedEventArgs e) => ShowBrowse();

    private async void RegenerateButton_Click(object sender, RoutedEventArgs e)
    {
        if (_topic is not null)
            await OpenTopicAsync(_topic, refresh: true);
    }

    private async void LessonRetryButton_Click(object sender, RoutedEventArgs e)
    {
        if (_topic is not null)
            await OpenTopicAsync(_topic);
    }

    private void PracticeButton_Click(object sender, RoutedEventArgs e)
    {
        if (_topic is not null)
        {
            _audio.Stop();
            PracticeRequested?.Invoke(_topic);
        }
    }

    private async void PlayButton_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is not Button { Tag: string text } || _draft is null)
            return;

        var settings = _draft.Settings;
        var voice = string.IsNullOrWhiteSpace(settings.TtsVoice) ? EdgeNeuralTtsService.DefaultVoice : settings.TtsVoice;
        _audio.Stop();
        await _audio.ToggleAsync(text, voice, WritingPresentation.NormalSpeed(settings.TtsSpeed));
    }

    private void GrammarPage_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (!IsVisible)
        {
            _audio.Stop();
            return;
        }

        // The explanation language may have changed on the Writing page.
        if (_topic is not null && IsLessonOpen && _lesson is not null && _lesson.Language != ExplanationLanguage)
            _ = OpenTopicAsync(_topic);
        else
            RenderBrowse();
    }

    private static Brush BrushFromHex(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        CancelLoading();
        SearchInput.PreviewKeyDown -= SearchInput_PreviewKeyDown;
        IsVisibleChanged -= GrammarPage_IsVisibleChanged;
        _history.Changed -= History_Changed;
        _audio.Dispose();
    }
}
