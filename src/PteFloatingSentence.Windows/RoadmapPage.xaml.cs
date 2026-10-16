using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using PteFloatingSentence.Core;
using PteFloatingSentence.Windows.Infrastructure;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Button = System.Windows.Controls.Button;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using Ellipse = System.Windows.Shapes.Ellipse;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using Orientation = System.Windows.Controls.Orientation;
using Rectangle = System.Windows.Shapes.Rectangle;
using UserControl = System.Windows.Controls.UserControl;

namespace PteFloatingSentence.Windows;

/// <summary>
/// The writing and grammar roadmap: today's guided session (review, learn, drill, apply) for the current stage, and
/// every stage with its progress, exercises, grammar topics and checklist. Progress comes from the Writing notebook.
/// </summary>
public partial class RoadmapPage : UserControl, IDisposable
{
    private WritingHistoryBook _history = WritingHistoryBook.InMemory();
    private IReadOnlyList<RoadmapStep> _plan = [];
    private bool _sessionActive;
    private bool _disposed;

    public RoadmapPage()
    {
        InitializeComponent();
        Render();
    }

    /// <summary>Asks the host to start (or resume) today's session.</summary>
    public event Action? StartSessionRequested;

    /// <summary>Asks the host to open the lesson for a grammar topic.</summary>
    public event Action<GrammarTopic>? TopicRequested;

    /// <summary>Asks the host to practice an exercise of a stage on the Writing page.</summary>
    public event Action<RoadmapStage, RoadmapPractice>? PracticeRequested;

    /// <summary>Today's plan as shown on the page.</summary>
    public IReadOnlyList<RoadmapStep> Plan => _plan;

    public RoadmapStage CurrentStage => _history.CurrentRoadmapStage();

    public bool IsSummaryVisible => SummaryCard.Visibility == Visibility.Visible;

    /// <summary>True while today's session runs; the start button then resumes it.</summary>
    public bool IsSessionActive
    {
        get => _sessionActive;
        set
        {
            _sessionActive = value;
            RenderToday();
        }
    }

    public void Initialize(WritingHistoryBook? history)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _history.Changed -= History_Changed;
        _history = history ?? WritingHistoryBook.InMemory();
        _history.Changed += History_Changed;
        Render();
    }

    private void History_Changed()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.InvokeAsync(History_Changed);
            return;
        }

        if (!_disposed)
            Render();
    }

    public void Render()
    {
        if (_disposed)
            return;

        RenderToday();
        RenderStages();
    }

    /// <summary>Makes <paramref name="stageKey"/> the current stage; null follows progress again.</summary>
    public void ChooseStage(string? stageKey) => _history.ChooseRoadmapStage(stageKey);

    public void ShowSummary(RoadmapSessionSummary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);
        SummaryTextBlock.Text = RoadmapPresentation.SummaryText(summary);
        SummaryStreakText.Text = RoadmapPresentation.StreakText(summary.Streak, _history.ChecksToday);
        SummaryCard.Visibility = Visibility.Visible;
    }

    public void HideSummary() => SummaryCard.Visibility = Visibility.Collapsed;

    // ---- Today ------------------------------------------------------------------------------------

    private void RenderToday()
    {
        var stage = _history.CurrentRoadmapStage();
        _plan = _history.TodayPlan();
        TodayTitleText.Text = RoadmapPresentation.TodayTitle(stage);
        StreakTextBlock.Text = RoadmapPresentation.StreakText(_history.Streak, _history.ChecksToday);
        StartSessionButton.Content = _sessionActive ? "Resume today's session" : RoadmapPresentation.StartButtonText(_plan);
        StartSessionButton.IsEnabled = _plan.Count > 0;
        TodayEmptyText.Visibility = _plan.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        TodayPlanPanel.Children.Clear();
        for (var index = 0; index < _plan.Count; index++)
            TodayPlanPanel.Children.Add(StepRow(_plan[index], index));
    }

    private UIElement StepRow(RoadmapStep step, int index)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, index == 0 ? 0 : 6, 0, 0) };
        row.Children.Add(new Border
        {
            Width = 22,
            Height = 22,
            CornerRadius = new CornerRadius(11),
            Background = BrushOf("#FF1E293B"),
            BorderBrush = BrushOf("#FF3B82F6"),
            BorderThickness = new Thickness(1),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = (index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture),
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = BrushOf("#FF93C5FD"),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        });
        row.Children.Add(new TextBlock
        {
            Text = RoadmapPresentation.StepGlyph(step.Kind),
            FontFamily = new System.Windows.Media.FontFamily("Segoe MDL2 Assets"),
            FontSize = 13,
            Foreground = BrushOf("#FF93C5FD"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 8, 0)
        });
        row.Children.Add(new TextBlock
        {
            Text = RoadmapPresentation.StepLine(step),
            FontSize = 13,
            Foreground = (Brush)FindResource("TextSecondaryBrush"),
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap
        });
        return row;
    }

    // ---- Stages -----------------------------------------------------------------------------------

    private void RenderStages()
    {
        StagesPanel.Children.Clear();
        var overview = _history.RoadmapOverview();
        var chosenKey = _history.Current.RoadmapStageKey;
        for (var index = 0; index < overview.Count; index++)
        {
            var (progress, state) = overview[index];
            StagesPanel.Children.Add(StageRow(progress, state, index == overview.Count - 1, progress.Stage.Key == chosenKey));
        }
    }

    private UIElement StageRow(RoadmapStageProgress progress, RoadmapStageState state, bool isLast, bool chosen)
    {
        var stage = progress.Stage;
        var stateBrush = BrushOf(RoadmapPresentation.StateColor(state));
        var row = new Grid { Tag = stage.Key };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        if (!isLast)
        {
            row.Children.Add(new Rectangle
            {
                Width = 2,
                Fill = state == RoadmapStageState.Done ? BrushOf("#8034D399") : BrushOf("#FF334155"),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 40, 0, 0)
            });
        }

        row.Children.Add(new Border
        {
            Width = 28,
            Height = 28,
            CornerRadius = new CornerRadius(14),
            Background = state switch
            {
                RoadmapStageState.Done => BrushOf("#FF34D399"),
                RoadmapStageState.Current => BrushOf("#FF2563EB"),
                _ => BrushOf("#FF1E293B")
            },
            BorderBrush = stateBrush,
            BorderThickness = new Thickness(2),
            VerticalAlignment = VerticalAlignment.Top,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 12, 0, 0),
            Child = new TextBlock
            {
                Text = state == RoadmapStageState.Done ? "✓" : stage.Number.ToString(System.Globalization.CultureInfo.InvariantCulture),
                FontSize = 12,
                FontWeight = FontWeights.Bold,
                Foreground = state is RoadmapStageState.Done or RoadmapStageState.Current ? Brushes.White : BrushOf("#FF94A3B8"),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        });

        var content = new StackPanel();
        var card = new Border
        {
            Background = (Brush)FindResource("CardSubtleBrush"),
            BorderBrush = state == RoadmapStageState.Current ? BrushOf("#FF3B82F6") : (Brush)FindResource("PanelBorderBrush"),
            BorderThickness = new Thickness(state == RoadmapStageState.Current ? 1.5 : 1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14, 12, 14, 12),
            Margin = new Thickness(6, 0, 0, 12),
            Opacity = state == RoadmapStageState.ComingSoon ? 0.65 : 1,
            Child = content
        };
        Grid.SetColumn(card, 1);
        row.Children.Add(card);

        var header = new WrapPanel();
        header.Children.Add(new TextBlock
        {
            Text = RoadmapPresentation.StageTitle(stage),
            FontSize = 14.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("TextPrimaryBrush"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 0)
        });
        header.Children.Add(Chip(RoadmapPresentation.StateLabel(state), RoadmapPresentation.StateColor(state)));
        header.Children.Add(Chip(stage.Cefr, "#FF94A3B8"));
        content.Children.Add(header);
        content.Children.Add(new TextBlock
        {
            Text = $"{stage.VietnameseTitle} · {stage.Goal}",
            FontSize = 12,
            Foreground = (Brush)FindResource("TextMutedBrush"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 0)
        });

        if (!stage.IsAvailable)
            return row;

        content.Children.Add(StageProgressBar(progress, state));

        content.Children.Add(SectionLabel("Exercises"));
        var practices = new WrapPanel();
        foreach (var practice in stage.Practices)
        {
            var button = new Button
            {
                Content = RoadmapPresentation.PracticeLabel(practice),
                Tag = practice,
                Style = (Style)FindResource("ChipButtonStyle"),
                ToolTip = RoadmapPresentation.PracticeTooltip(practice)
            };
            button.Click += (_, _) => PracticeRequested?.Invoke(stage, practice);
            practices.Children.Add(button);
        }

        content.Children.Add(practices);

        content.Children.Add(SectionLabel("Grammar"));
        var topics = new WrapPanel();
        foreach (var topic in progress.Topics)
            topics.Children.Add(TopicChip(topic));
        content.Children.Add(topics);

        if (state == RoadmapStageState.Current)
            content.Children.Add(Checklist(progress, chosen));
        else if (state != RoadmapStageState.ComingSoon)
        {
            var choose = new Button
            {
                Content = state == RoadmapStageState.Done ? "Practice this stage again" : "Set as my stage",
                Tag = stage.Key,
                Style = (Style)FindResource("SecondaryButtonStyle"),
                Padding = new Thickness(10, 4, 10, 4),
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 6, 0, 0),
                ToolTip = "Make this the stage of today's session"
            };
            choose.Click += (_, _) => ChooseStage(stage.Key);
            content.Children.Add(choose);
        }

        return row;
    }

    private UIElement StageProgressBar(RoadmapStageProgress progress, RoadmapStageState state)
    {
        var bar = new Grid { Margin = new Thickness(0, 10, 0, 2) };
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var track = new Grid { Height = 6, VerticalAlignment = VerticalAlignment.Center };
        track.Children.Add(new Border { Background = BrushOf("#FF1E293B"), CornerRadius = new CornerRadius(3) });
        var fill = new Grid();
        fill.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0, progress.Percent), GridUnitType.Star) });
        fill.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0, 100 - progress.Percent), GridUnitType.Star) });
        fill.Children.Add(new Border
        {
            Background = state == RoadmapStageState.Done || progress.IsComplete ? BrushOf("#FF34D399") : BrushOf("#FF3B82F6"),
            CornerRadius = new CornerRadius(3)
        });
        track.Children.Add(fill);
        bar.Children.Add(track);

        var percent = new TextBlock
        {
            Text = RoadmapPresentation.PercentText(progress),
            FontSize = 12,
            Foreground = (Brush)FindResource("TextMutedBrush"),
            Margin = new Thickness(10, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(percent, 1);
        bar.Children.Add(percent);
        return bar;
    }

    private Button TopicChip(RoadmapTopicProgress topic)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        content.Children.Add(new Ellipse
        {
            Width = 8,
            Height = 8,
            Fill = BrushOf(RoadmapPresentation.TopicColor(topic)),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 7, 0)
        });
        content.Children.Add(new TextBlock { Text = topic.Topic.Name, VerticalAlignment = VerticalAlignment.Center });
        var button = new Button
        {
            Content = content,
            Tag = topic.Topic,
            Style = (Style)FindResource("ChipButtonStyle"),
            ToolTip = RoadmapPresentation.TopicTooltip(topic)
        };
        button.Click += (_, _) => TopicRequested?.Invoke(topic.Topic);
        return button;
    }

    private UIElement Checklist(RoadmapStageProgress progress, bool chosen)
    {
        var panel = new StackPanel();
        var box = new Border
        {
            Background = BrushOf("#FF111827"),
            BorderBrush = (Brush)FindResource("PanelBorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 10, 12, 10),
            Margin = new Thickness(0, 6, 0, 0),
            Child = panel
        };

        panel.Children.Add(new TextBlock
        {
            Text = "To finish this stage",
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("TextMutedBrush"),
            Margin = new Thickness(0, 0, 0, 6)
        });
        panel.Children.Add(CheckLine(progress.LessonsComplete, RoadmapPresentation.ChecklistLessons(progress)));
        panel.Children.Add(CheckLine(progress.DrillsComplete, RoadmapPresentation.ChecklistDrills(progress)));
        panel.Children.Add(CheckLine(progress.SentencesComplete, RoadmapPresentation.ChecklistSentences(progress)));
        panel.Children.Add(CheckLine(progress.NeedsReview.Count == 0, RoadmapPresentation.ChecklistReview(progress)));

        var footer = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        footer.Children.Add(new TextBlock
        {
            Text = RoadmapPresentation.StageModeText(chosen),
            FontSize = 12,
            Foreground = (Brush)FindResource("TextMutedBrush"),
            VerticalAlignment = VerticalAlignment.Center
        });
        if (chosen)
        {
            var follow = new Button
            {
                Content = "Follow my progress",
                Tag = "follow",
                Style = (Style)FindResource("SecondaryButtonStyle"),
                Padding = new Thickness(10, 3, 10, 3),
                Margin = new Thickness(10, 0, 0, 0),
                ToolTip = "Let the roadmap pick the first stage you have not finished"
            };
            follow.Click += (_, _) => ChooseStage(null);
            footer.Children.Add(follow);
        }

        panel.Children.Add(footer);
        return box;
    }

    private UIElement CheckLine(bool done, string text)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };
        row.Children.Add(new TextBlock
        {
            Text = RoadmapPresentation.ChecklistMark(done),
            Width = 18,
            FontSize = 13,
            FontWeight = FontWeights.Bold,
            Foreground = done ? BrushOf("#FF34D399") : BrushOf("#FF64748B")
        });
        row.Children.Add(new TextBlock
        {
            Text = text,
            FontSize = 12.5,
            Foreground = done ? (Brush)FindResource("TextSecondaryBrush") : (Brush)FindResource("TextMutedBrush"),
            TextWrapping = TextWrapping.Wrap
        });
        return row;
    }

    private TextBlock SectionLabel(string text) => new()
    {
        Text = text,
        Style = (Style)FindResource("SectionLabelStyle"),
        Margin = new Thickness(0, 10, 0, 6)
    };

    private static Border Chip(string text, string color) => new()
    {
        CornerRadius = new CornerRadius(10),
        BorderThickness = new Thickness(1),
        BorderBrush = BrushOf(color),
        Padding = new Thickness(8, 1, 8, 1),
        Margin = new Thickness(0, 0, 6, 0),
        VerticalAlignment = VerticalAlignment.Center,
        Child = new TextBlock { Text = text, FontSize = 11, Foreground = BrushOf(color) }
    };

    private static SolidColorBrush BrushOf(string hex) => new((Color)ColorConverter.ConvertFromString(hex));

    // ---- Buttons ----------------------------------------------------------------------------------

    private void StartSessionButton_Click(object sender, RoutedEventArgs e) => StartSessionRequested?.Invoke();

    private void CloseSummaryButton_Click(object sender, RoutedEventArgs e) => HideSummary();

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _history.Changed -= History_Changed;
    }
}
