using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using Cursors = System.Windows.Input.Cursors;
using PteFloatingSentence.Core;

namespace PteFloatingSentence.Windows;

public partial class FloatingWindow : Window
{
    private bool _hasMultipleSentences;
    private Guid _currentSentenceId;

    public FloatingWindow()
    {
        InitializeComponent();
    }

    public event EventHandler? SettingsRequested;
    public event EventHandler? ExitRequested;
    public event EventHandler? PreviousRequested;
    public event EventHandler? NextRequested;
    public event EventHandler<(double Left, double Top)>? PositionChanged;
    public event EventHandler<string>? VocabularySelected;
    public event EventHandler<Guid>? VocabularyClicked;
    public event EventHandler<(Guid SentenceId, Guid ItemId)>? HideVocabularyRequested;
    public event EventHandler<(Guid SentenceId, Guid ItemId)>? RetryVocabularyRequested;

    public void ApplySettings(AppSettings settings)
    {
        var defaults = AppSettings.Default;
        var activeList = StudyListRules.ActiveList(settings);
        var sentenceCount = activeList.Sentences.Count;
        _hasMultipleSentences = sentenceCount > 1;
        PreviousButton.IsEnabled = _hasMultipleSentences;
        NextButton.IsEnabled = _hasMultipleSentences;
        UpdateNavigationVisibility(isPointerOver: IsMouseOver);

        StudySentence? currentSentence = sentenceCount == 0 ? null : activeList.Sentences[activeList.CurrentSentenceIndex];
        _currentSentenceId = currentSentence?.Id ?? Guid.Empty;

        var text = currentSentence is null
            ? "Add a sentence in Settings."
            : currentSentence.Text;

        ProgressText.Text = $"{activeList.Name} · {(sentenceCount == 0 ? 0 : activeList.CurrentSentenceIndex + 1)} / {sentenceCount}";
        var fontSize = IsValidFontSize(settings.FontSize) ? settings.FontSize : defaults.FontSize;
        var foregroundBrush = ToBrush(settings.TextColor, defaults.TextColor);
        SentenceBackground.Background = ToBlackBackground(settings.BackgroundOpacity, defaults.BackgroundOpacity);

        RenderSentenceDocument(text, currentSentence?.Vocabulary, fontSize, foregroundBrush);
        RenderVocabularyPanel(currentSentence?.Vocabulary);

        SentenceCard.Measure(new System.Windows.Size(900, double.PositiveInfinity));
        ApplyNavigationButtonSize(SentenceCard.DesiredSize.Height);
    }

    private void RenderSentenceDocument(string text, IReadOnlyList<VocabularyItem>? vocabulary, double fontSize, Brush foregroundBrush)
    {
        SentenceDocument.Blocks.Clear();
        var paragraph = new Paragraph
        {
            FontSize = fontSize,
            Foreground = foregroundBrush,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0),
            Padding = new Thickness(0)
        };

        if (vocabulary is null || vocabulary.Count == 0)
        {
            paragraph.Inlines.Add(new Run(text));
            SentenceDocument.Blocks.Add(paragraph);
            return;
        }

        // Highlight matching vocabulary phrases
        var sortedVocab = vocabulary
            .Where(v => !string.IsNullOrWhiteSpace(v.Phrase))
            .OrderByDescending(v => v.Phrase.Length)
            .ToList();

        var matches = new List<(int Start, int Length, VocabularyItem Item)>();
        foreach (var item in sortedVocab)
        {
            var searchIndex = 0;
            while (searchIndex < text.Length)
            {
                var index = text.IndexOf(item.Phrase, searchIndex, StringComparison.OrdinalIgnoreCase);
                if (index < 0)
                    break;

                var length = item.Phrase.Length;
                var overlaps = matches.Any(m => Math.Max(m.Start, index) < Math.Min(m.Start + m.Length, index + length));
                if (!overlaps)
                {
                    matches.Add((index, length, item));
                }

                searchIndex = index + Math.Max(1, length);
            }
        }

        matches = matches.OrderBy(m => m.Start).ToList();

        var cursor = 0;
        foreach (var (start, length, item) in matches)
        {
            if (start > cursor)
            {
                paragraph.Inlines.Add(new Run(text.Substring(cursor, start - cursor)));
            }

            var span = new Span(new Run(text.Substring(start, length)))
            {
                Foreground = new SolidColorBrush(Color.FromRgb(0x5E, 0xEA, 0xD4)),
                TextDecorations = TextDecorations.Underline,
                Cursor = Cursors.Hand
            };
            var itemId = item.Id;
            span.MouseLeftButtonDown += (_, e) =>
            {
                e.Handled = true;
                VocabularyClicked?.Invoke(this, itemId);
            };

            paragraph.Inlines.Add(span);
            cursor = start + length;
        }

        if (cursor < text.Length)
        {
            paragraph.Inlines.Add(new Run(text.Substring(cursor)));
        }

        SentenceDocument.Blocks.Add(paragraph);
    }

    private void RenderVocabularyPanel(IReadOnlyList<VocabularyItem>? vocabulary)
    {
        var visibleItems = (vocabulary ?? [])
            .Where(item => !item.IsHidden)
            .ToList();

        if (visibleItems.Count == 0)
        {
            VocabularyPanel.Visibility = Visibility.Collapsed;
            VocabularyPanel.ItemsSource = null;
            return;
        }

        var cards = visibleItems.Select(item => new VocabularyCardViewModel
        {
            ItemId = item.Id,
            Phrase = item.Phrase,
            Meaning = item.Meaning,
            Example = item.Example,
            PronunciationIpa = item.PronunciationIpa,
            Status = item.Status,
            ErrorMessage = item.LastError
        }).ToList();

        VocabularyPanel.ItemsSource = cards;
        VocabularyPanel.Visibility = Visibility.Visible;
    }

    private void SentenceBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        HandleSelection();
    }

    private void SentenceBox_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        HandleSelection();
    }

    private void HandleSelection()
    {
        var selection = SentenceBox.Selection.Text;
        if (string.IsNullOrWhiteSpace(selection))
            return;

        var trimmed = selection.Trim();
        if (trimmed.Length > 0)
        {
            VocabularySelected?.Invoke(this, trimmed);
        }
    }

    private void HideVocabularyButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: Guid itemId })
        {
            HideVocabularyRequested?.Invoke(this, (_currentSentenceId, itemId));
        }
    }

    private void RetryVocabularyButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: Guid itemId })
        {
            RetryVocabularyRequested?.Invoke(this, (_currentSentenceId, itemId));
        }
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (IsWithinButtonTree(e.OriginalSource, PreviousButton) ||
            IsWithinButtonTree(e.OriginalSource, NextButton) ||
            IsWithinElementTree(e.OriginalSource, SentenceBox) ||
            IsWithinElementTree(e.OriginalSource, VocabularyPanel))
        {
            return;
        }

        DragMove();
        PositionChanged?.Invoke(this, (Left, Top));
    }

    private void Window_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e) => UpdateNavigationVisibility(isPointerOver: true);

    private void Window_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e) => UpdateNavigationVisibility(isPointerOver: false);

    private void PreviousButton_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        PreviousRequested?.Invoke(this, EventArgs.Empty);
    }

    private void NextButton_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        NextRequested?.Invoke(this, EventArgs.Empty);
    }

    private void SettingsMenuItem_Click(object sender, RoutedEventArgs e) => SettingsRequested?.Invoke(this, EventArgs.Empty);

    private void ExitMenuItem_Click(object sender, RoutedEventArgs e) => ExitRequested?.Invoke(this, EventArgs.Empty);

    private void UpdateNavigationVisibility(bool isPointerOver)
    {
        if (!_hasMultipleSentences)
        {
            PreviousButton.Visibility = Visibility.Collapsed;
            NextButton.Visibility = Visibility.Collapsed;
            return;
        }

        var visibility = isPointerOver ? Visibility.Visible : Visibility.Hidden;
        PreviousButton.Visibility = visibility;
        NextButton.Visibility = visibility;
    }

    private void SentenceCard_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        ApplyNavigationButtonSize(e.NewSize.Height);
    }

    private void ApplyNavigationButtonSize(double height)
    {
        var clampedHeight = Math.Round(height);
        if (clampedHeight is >= 24 and <= 250)
        {
            PreviousButton.Width = clampedHeight;
            PreviousButton.Height = clampedHeight;
            NextButton.Width = clampedHeight;
            NextButton.Height = clampedHeight;
            var iconSize = Math.Max(14, Math.Round(clampedHeight * 0.38));
            PreviousButton.FontSize = iconSize;
            NextButton.FontSize = iconSize;
        }
    }

    private static bool IsWithinButtonTree(object originalSource, System.Windows.Controls.Button button)
    {
        for (var current = originalSource as DependencyObject; current is not null; current = GetVisualParent(current))
        {
            if (ReferenceEquals(current, button))
                return true;
        }

        return false;
    }

    private static bool IsWithinElementTree(object originalSource, FrameworkElement element)
    {
        for (var current = originalSource as DependencyObject; current is not null; current = GetVisualParent(current))
        {
            if (ReferenceEquals(current, element))
                return true;
        }

        return false;
    }

    private static DependencyObject? GetVisualParent(DependencyObject element) =>
        element is Visual or System.Windows.Media.Media3D.Visual3D
            ? VisualTreeHelper.GetParent(element)
            : null;

    private static bool IsValidFontSize(double value) => !double.IsNaN(value) && !double.IsInfinity(value) && value is >= 12 and <= 96;

    private static System.Windows.Media.Brush ToBrush(string? textColor, string fallbackColor)
    {
        try
        {
            var color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(textColor ?? fallbackColor)!;
            return new SolidColorBrush(color);
        }
        catch (Exception)
        {
            return new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(fallbackColor)!);
        }
    }

    private static System.Windows.Media.Brush ToBlackBackground(double opacity, double fallbackOpacity)
    {
        var selectedOpacity = !double.IsNaN(opacity) && !double.IsInfinity(opacity) && opacity is >= 0 and <= 1
            ? opacity
            : fallbackOpacity;
        return new SolidColorBrush(System.Windows.Media.Color.FromArgb((byte)Math.Round(selectedOpacity * byte.MaxValue), 0, 0, 0));
    }
}

public sealed class VocabularyCardViewModel
{
    public Guid ItemId { get; init; }
    public string Phrase { get; init; } = string.Empty;
    public string? Meaning { get; init; }
    public string? Example { get; init; }
    public string? PronunciationIpa { get; init; }
    public VocabularyStatus Status { get; init; }
    public string? ErrorMessage { get; init; }

    public Visibility IpaVisibility => string.IsNullOrWhiteSpace(PronunciationIpa) ? Visibility.Collapsed : Visibility.Visible;
    public Visibility ContentVisibility => Status == VocabularyStatus.Ready ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ErrorVisibility => Status == VocabularyStatus.Failed ? Visibility.Visible : Visibility.Collapsed;
    public Visibility StatusVisibility => Status == VocabularyStatus.Pending ? Visibility.Visible : Visibility.Collapsed;
    public Visibility RetryVisibility => Status == VocabularyStatus.Failed ? Visibility.Visible : Visibility.Collapsed;
    public string StatusText => Status switch
    {
        VocabularyStatus.Pending => "Loading explanation...",
        VocabularyStatus.Failed => "Failed",
        _ => string.Empty
    };
    public Brush StatusBrush => Status == VocabularyStatus.Failed
        ? new SolidColorBrush(Color.FromRgb(0xFC, 0xA5, 0xA5))
        : new SolidColorBrush(Color.FromRgb(0x94, 0xA3, 0xB8));
}
