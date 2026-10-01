using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using Cursors = System.Windows.Input.Cursors;
using PteFloatingSentence.Core;
using PteFloatingSentence.Windows.Infrastructure;

namespace PteFloatingSentence.Windows;

public partial class FloatingWindow : Window
{
    private bool _hasMultipleSentences;
    private Guid _currentListId;
    private Guid _currentSentenceId;
    private string? _currentSentenceText;
    private ReviewPracticeSession? _practiceSession;
    private AppSettings _settings = AppSettings.Default;

    private IAudioPlayer _audioPlayer;
    private ITtsService _ttsService;
    private IAudioCacheManager _audioCacheManager;
    private ISentenceAudioPlayback _audioPlayback;

    private const string AudioGlyphIdle = "\uE767";
    private const string AudioGlyphActive = "\uE768";
    private static readonly Brush AudioBrushIdle = CreateFrozenBrush(Color.FromRgb(0x9C, 0xA3, 0xAF));
    private static readonly Brush AudioBrushActive = CreateFrozenBrush(Color.FromRgb(0x5E, 0xEA, 0xD4));

    private static Brush CreateFrozenBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    public bool IsPracticeMode { get; private set; }
    public event EventHandler? PracticeModeChanged;
    public event EventHandler<(Guid ListId, Guid SentenceId, bool Completed)>? SentenceCompleted;

    public IAudioPlayer AudioPlayer
    {
        get => _audioPlayer;
        set
        {
            if (ReferenceEquals(_audioPlayer, value)) return;
            _audioPlayer = value ?? new WpfAudioPlayer();
            RecreateAudioPlayback(playerReplaced: true);
        }
    }

    public ITtsService TtsService
    {
        get => _ttsService;
        set
        {
            _ttsService = value ?? new EdgeNeuralTtsService();
            RecreateAudioPlayback();
        }
    }

    public IAudioCacheManager AudioCacheManager
    {
        get => _audioCacheManager;
        set
        {
            _audioCacheManager = value ?? new AudioCacheManager();
            RecreateAudioPlayback();
        }
    }

    public FloatingWindow() : this(null, null, null)
    {
    }

    public FloatingWindow(
        IAudioPlayer? audioPlayer = null,
        ITtsService? ttsService = null,
        IAudioCacheManager? audioCacheManager = null)
    {
        _audioPlayer = audioPlayer ?? new WpfAudioPlayer();
        _ttsService = ttsService ?? new EdgeNeuralTtsService();
        _audioCacheManager = audioCacheManager ?? new AudioCacheManager();
        _audioPlayback = CreateAudioPlayback();

        InitializeComponent();
        IsVisibleChanged += FloatingWindow_IsVisibleChanged;
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
    public event EventHandler<(Guid SentenceId, Guid ItemId)>? DeleteVocabularyRequested;

    private static readonly Color[] VocabularyPalette =
    [
        Color.FromRgb(0x5E, 0xEA, 0xD4), // Teal (#5EEAD4)
        Color.FromRgb(0xFB, 0xBF, 0x24), // Amber / Warm Gold (#FBBF24)
        Color.FromRgb(0x38, 0xBD, 0xF8), // Sky Blue (#38BDF8)
        Color.FromRgb(0xF4, 0x72, 0xB6), // Pink (#F472B6)
        Color.FromRgb(0xA7, 0x8B, 0xFA), // Lavender / Purple (#A78BFA)
        Color.FromRgb(0xFB, 0x92, 0x3C), // Coral / Orange (#FB923C)
        Color.FromRgb(0x4A, 0xDE, 0x80), // Mint Green (#4ADE80)
        Color.FromRgb(0xE8, 0x79, 0xF9), // Fuchsia (#E879F9)
    ];

    private IReadOnlyList<VocabularyItem>? _currentVocabulary;
    private Guid? _highlightedItemId;
    private RenderSignature? _renderSignature;
    private bool _showVocabularyCards = true;
    private RenderSignature? _vocabularyRenderSignature;
    private Guid? _vocabularyRenderHighlight;
    private bool? _vocabularyRenderVisibility;

    public void ApplySettings(AppSettings settings) => ApplySettings(settings, renderContent: true);

    public void ApplySettings(AppSettings settings, bool renderContent)
    {
        var practicePreferencesChanged = _settings.PracticeMode != settings.PracticeMode ||
            _settings.TtsVoice != settings.TtsVoice || _settings.TtsSpeed != settings.TtsSpeed;
        _settings = settings;
        var defaults = AppSettings.Default;
        var activeList = StudyListRules.ActiveList(settings);
        var sentenceCount = activeList.Sentences.Count;
        _hasMultipleSentences = sentenceCount > 1;
        PreviousButton.IsEnabled = _hasMultipleSentences;
        NextButton.IsEnabled = _hasMultipleSentences;
        UpdateNavigationVisibility(isPointerOver: IsMouseOver);

        StudySentence? currentSentence = sentenceCount == 0 ? null : activeList.Sentences[activeList.CurrentSentenceIndex];
        var newSentenceId = currentSentence?.Id ?? Guid.Empty;
        var newSentenceText = currentSentence?.Text;
        if (_currentListId != activeList.Id ||
            _currentSentenceId != newSentenceId ||
            !string.Equals(_currentSentenceText, newSentenceText, StringComparison.Ordinal))
        {
            StopAndResetAudio();
        }
        _currentListId = activeList.Id;
        _currentSentenceId = newSentenceId;
        _currentSentenceText = newSentenceText;
        _currentVocabulary = currentSentence?.Vocabulary;

        var text = currentSentence is null
            ? "Add a sentence in Settings."
            : currentSentence.Text;

        if (AudioButton is not null)
        {
            AudioButton.IsEnabled = currentSentence is not null && !string.IsNullOrWhiteSpace(currentSentence.Text);
        }

        ProgressText.Text = $"{activeList.Name} · {(sentenceCount == 0 ? 0 : activeList.CurrentSentenceIndex + 1)} / {sentenceCount}";
        var fontSize = IsValidFontSize(settings.FontSize) ? settings.FontSize : defaults.FontSize;
        var foregroundBrush = ToBrush(settings.TextColor, defaults.TextColor);
        SentenceBackground.Background = ToBlackBackground(settings.BackgroundOpacity, defaults.BackgroundOpacity);

        if (IsPracticeMode)
        {
            if (_practiceSession is not null)
            {
                var practiceList = settings.StudyLists.FirstOrDefault(l => l.Id == _practiceSession.List.Id) ?? activeList;
                var practiceContentChanged = _practiceSession.RefreshList(practiceList);
                _hasMultipleSentences = _practiceSession.List.Sentences.Count > 1;
                PreviousButton.IsEnabled = NextButton.IsEnabled = _hasMultipleSentences;
                UpdateNavigationVisibility(isPointerOver: IsMouseOver);
                if (practicePreferencesChanged || practiceContentChanged) FloatingPracticePanel.Load(_practiceSession, settings, _audioPlayback);
            }
            SentenceBox.Visibility = Visibility.Collapsed;
            NormalSentenceContainer.Visibility = Visibility.Collapsed;
            PracticeContainer.Visibility = Visibility.Visible;
            VocabularyPanel.Visibility = Visibility.Collapsed;
            return;
        }

        SentenceBox.Visibility = Visibility.Visible;
        NormalSentenceContainer.Visibility = Visibility.Visible;
        PracticeContainer.Visibility = Visibility.Collapsed;

        var vocabulary = currentSentence?.Vocabulary ?? [];
        _showVocabularyCards = settings.ShowVocabularyCards;
        SentenceBox.IsHitTestVisible = _showVocabularyCards;
        SentenceBox.Cursor = _showVocabularyCards ? Cursors.IBeam : Cursors.Arrow;
        var sentenceVocabulary = _showVocabularyCards ? vocabulary : [];
        var phraseBreaks = currentSentence?.PhraseBreakAfterWordIndices ?? [];
        var sentenceSignature = RenderSignature.Create(text, fontSize, settings.TextColor, settings.BackgroundOpacity, sentenceVocabulary, phraseBreaks);
        var vocabularySignature = RenderSignature.Create(string.Empty, 0, string.Empty, 0, vocabulary);
        if (!renderContent)
        {
            VocabularyPanel.Visibility = Visibility.Collapsed;
            VocabularyPanel.ItemsSource = null;
            _vocabularyRenderSignature = null;
            _vocabularyRenderHighlight = null;
            _vocabularyRenderVisibility = null;
            return;
        }

        if (_renderSignature != sentenceSignature)
        {
            RenderSentenceDocument(text, sentenceVocabulary, fontSize, foregroundBrush, phraseBreaks);
            _renderSignature = sentenceSignature;
        }

        if (_vocabularyRenderSignature != vocabularySignature ||
            _vocabularyRenderHighlight != _highlightedItemId ||
            _vocabularyRenderVisibility != _showVocabularyCards)
        {
            RenderVocabularyPanel(vocabulary);
            _vocabularyRenderSignature = vocabularySignature;
            _vocabularyRenderHighlight = _highlightedItemId;
            _vocabularyRenderVisibility = _showVocabularyCards;
        }

        SentenceCard.InvalidateMeasure();
        SentenceCard.Measure(new System.Windows.Size(900, double.PositiveInfinity));
        ApplyNavigationButtonSize(SentenceCard.DesiredSize.Height);
    }

    public void FocusVocabularyItem(Guid itemId)
    {
        _highlightedItemId = itemId;
        if (_showVocabularyCards)
        {
            RenderVocabularyPanel(_currentVocabulary);
            _vocabularyRenderHighlight = itemId;
        }
    }

    private static Dictionary<Guid, Color> BuildColorMap(IReadOnlyList<VocabularyItem>? vocabulary)
    {
        var map = new Dictionary<Guid, Color>();
        if (vocabulary is null)
            return map;

        for (int i = 0; i < vocabulary.Count; i++)
        {
            map[vocabulary[i].Id] = VocabularyPalette[i % VocabularyPalette.Length];
        }

        return map;
    }

    private void RenderSentenceDocument(string text, IReadOnlyList<VocabularyItem>? vocabulary, double fontSize, Brush foregroundBrush, IReadOnlyList<int> phraseBreaks)
    {
        ClearSentenceDocumentInlines();
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
            paragraph.Inlines.Add(new Run(SentencePhrasing.GetDisplayText(text, phraseBreaks)));
            SentenceDocument.Blocks.Add(paragraph);
            return;
        }

        var colorMap = BuildColorMap(vocabulary);

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
                paragraph.Inlines.Add(new Run(SentencePhrasing.GetDisplayRange(text, cursor, start - cursor, phraseBreaks)));
            }

            var itemColor = colorMap.TryGetValue(item.Id, out var c) ? c : VocabularyPalette[0];
            var itemBrush = new SolidColorBrush(itemColor);
            var hoverColor = GetHoverColor(itemColor);
            var hoverBrush = new SolidColorBrush(hoverColor);
            var hoverBackground = new SolidColorBrush(Color.FromArgb(0x33, itemColor.R, itemColor.G, itemColor.B));

            var run = new Run(SentencePhrasing.GetDisplayRange(text, start, length, phraseBreaks))
            {
                Tag = item.Id,
                Cursor = Cursors.Hand
            };
            var span = new Span(run)
            {
                Tag = item.Id,
                Foreground = itemBrush,
                TextDecorations = TextDecorations.Underline,
                Cursor = Cursors.Hand,
                ToolTip = "Click to view explanation"
            };
            span.Resources["OriginalBrush"] = itemBrush;
            span.Resources["HoverBrush"] = hoverBrush;
            span.Resources["HoverBackground"] = hoverBackground;

            span.MouseEnter += OnVocabularySpanMouseEnter;
            span.MouseLeave += OnVocabularySpanMouseLeave;
            span.QueryCursor += OnVocabularySpanQueryCursor;
            run.QueryCursor += OnVocabularySpanQueryCursor;

            paragraph.Inlines.Add(span);
            cursor = start + length;
        }

        if (cursor < text.Length)
        {
            paragraph.Inlines.Add(new Run(SentencePhrasing.GetDisplayRange(text, cursor, text.Length - cursor, phraseBreaks)));
        }

        SentenceDocument.Blocks.Add(paragraph);
    }

    private void RenderVocabularyPanel(IReadOnlyList<VocabularyItem>? vocabulary)
    {
        if (!_showVocabularyCards)
        {
            VocabularyPanel.Visibility = Visibility.Collapsed;
            VocabularyPanel.ItemsSource = null;
            return;
        }

        var visibleItems = (vocabulary ?? [])
            .Where(item => !item.IsHidden)
            .ToList();

        if (visibleItems.Count == 0)
        {
            VocabularyPanel.Visibility = Visibility.Collapsed;
            VocabularyPanel.ItemsSource = null;
            return;
        }

        var colorMap = BuildColorMap(vocabulary);

        var cards = visibleItems.Select(item =>
        {
            var itemColor = colorMap.TryGetValue(item.Id, out var c) ? c : VocabularyPalette[0];
            var isHighlighted = _highlightedItemId == item.Id;

            return new VocabularyCardViewModel
            {
                ItemId = item.Id,
                Phrase = item.Phrase,
                Meaning = item.Meaning,
                Example = item.Example,
                PronunciationIpa = item.PronunciationIpa,
                Status = item.Status,
                ErrorMessage = item.LastError,
                AccentBrush = new SolidColorBrush(itemColor),
                BorderBrush = isHighlighted
                    ? new SolidColorBrush(Color.FromArgb(0xFF, itemColor.R, itemColor.G, itemColor.B))
                    : new SolidColorBrush(Color.FromArgb(0x44, itemColor.R, itemColor.G, itemColor.B)),
                CardBorderThickness = isHighlighted ? new Thickness(2) : new Thickness(1)
            };
        }).ToList();

        VocabularyPanel.ItemsSource = cards;
        VocabularyPanel.Visibility = Visibility.Visible;
    }

    private void SentenceBox_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        var selection = SentenceBox.Selection.Text;
        if (!string.IsNullOrWhiteSpace(selection))
        {
            HandleSelection();
            SentenceBox.ReleaseMouseCapture();
            Mouse.Capture(null);
            Keyboard.ClearFocus();
            e.Handled = true;
            return;
        }

        var mousePos = e.GetPosition(SentenceBox);
        var pointer = SentenceBox.GetPositionFromPoint(mousePos, snapToText: false);
        if (pointer is not null)
        {
            var itemId = FindVocabularyItemId(pointer.Parent as TextElement);
            if (itemId.HasValue)
            {
                e.Handled = true;
                VocabularyClicked?.Invoke(this, itemId.Value);
                SentenceBox.Selection.Select(SentenceBox.Document.ContentStart, SentenceBox.Document.ContentStart);
                SentenceBox.ReleaseMouseCapture();
                Mouse.Capture(null);
                Keyboard.ClearFocus();
                return;
            }
        }

        SentenceBox.Selection.Select(SentenceBox.Document.ContentStart, SentenceBox.Document.ContentStart);
        SentenceBox.ReleaseMouseCapture();
        Mouse.Capture(null);
        Keyboard.ClearFocus();
        e.Handled = true;
    }

    private void SentenceBox_PreviewTouchDown(object sender, TouchEventArgs e)
    {
        SentenceBox.Selection.Select(SentenceBox.Document.ContentStart, SentenceBox.Document.ContentStart);
        Focus();
        e.Handled = true;
    }

    private void HandleSelection()
    {
        var selection = SentenceBox.Selection.Text;
        if (string.IsNullOrWhiteSpace(selection))
            return;

        var trimmed = selection.Trim();
        var cleaned = VocabularyRules.NormalizePhrase(VocabularyRules.TrimPunctuation(trimmed));

        // Require at least 2 characters to avoid triggering on accidental single-char selections or punctuation
        if (cleaned.Length < 2)
        {
            SentenceBox.Selection.Select(SentenceBox.Document.ContentStart, SentenceBox.Document.ContentStart);
            return;
        }

        // 1. Check if the selection is inside an existing vocabulary span
        var startItemId = FindVocabularyItemId(SentenceBox.Selection.Start.Parent as TextElement);
        var endItemId = FindVocabularyItemId(SentenceBox.Selection.End.Parent as TextElement);
        var itemIdFromSpan = startItemId.HasValue && startItemId == endItemId
            ? startItemId
            : null;

        if (itemIdFromSpan.HasValue)
        {
            VocabularyClicked?.Invoke(this, itemIdFromSpan.Value);
            SentenceBox.Selection.Select(SentenceBox.Document.ContentStart, SentenceBox.Document.ContentStart);
            return;
        }

        // 2. Check if the selection text matches any existing vocabulary in the sentence
        var existing = VocabularyRules.FindEquivalent(_currentVocabulary, cleaned);
        if (existing is not null)
        {
            VocabularyClicked?.Invoke(this, existing.Id);
            SentenceBox.Selection.Select(SentenceBox.Document.ContentStart, SentenceBox.Document.ContentStart);
            return;
        }

        // 3. New phrase: clear selection and release capture BEFORE invoking
        SentenceBox.Selection.Select(SentenceBox.Document.ContentStart, SentenceBox.Document.ContentStart);
        SentenceBox.ReleaseMouseCapture();
        Mouse.Capture(null);
        Keyboard.ClearFocus();

        VocabularySelected?.Invoke(this, cleaned);
    }

    private static Guid? FindVocabularyItemId(TextElement? element)
    {
        for (var cur = element; cur is not null; cur = cur.Parent as TextElement)
        {
            if (cur.Tag is Guid id)
                return id;
        }

        return null;
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

    private void DeleteVocabularyButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: Guid itemId })
        {
            DeleteVocabularyRequested?.Invoke(this, (_currentSentenceId, itemId));
        }
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (IsWithinButtonTree(e.OriginalSource, PreviousButton) ||
            IsWithinButtonTree(e.OriginalSource, NextButton) ||
            (AudioButton is not null && IsWithinButtonTree(e.OriginalSource, AudioButton)) ||
            IsWithinElementTree(e.OriginalSource, SentenceBox) ||
            IsWithinElementTree(e.OriginalSource, VocabularyPanel))
        {
            return;
        }

        if (e.ButtonState == MouseButtonState.Pressed)
        {
            try
            {
                DragMove();
                PositionChanged?.Invoke(this, (Left, Top));
            }
            catch (InvalidOperationException)
            {
            }
        }
    }

    public void StartPractice(Guid? listId = null)
    {
        StopAndResetAudio();
        var targetList = listId.HasValue
            ? _settings.StudyLists.FirstOrDefault(l => l.Id == listId.Value)
            : StudyListRules.ActiveList(_settings);

        if (targetList is null)
            return;

        _practiceSession = new ReviewPracticeSession(targetList, onSentenceCompleted: (sentenceId, completed) =>
        {
            SentenceCompleted?.Invoke(this, (_practiceSession!.List.Id, sentenceId, completed));
        });

        if (_practiceSession.IsAllSentencesCompleted && targetList.Sentences.Count > 0)
        {
            _practiceSession.RestartList();
        }

        IsPracticeMode = true;
        SentenceBox.Visibility = Visibility.Collapsed;
        NormalSentenceContainer.Visibility = Visibility.Collapsed;
        PracticeContainer.Visibility = Visibility.Visible;
        VocabularyPanel.Visibility = Visibility.Collapsed;

        if (PracticeMenuItem is not null)
        {
            PracticeMenuItem.Header = "Exit Practice";
        }

        _hasMultipleSentences = targetList.Sentences.Count > 1;
        PreviousButton.IsEnabled = _hasMultipleSentences;
        NextButton.IsEnabled = _hasMultipleSentences;
        UpdateNavigationVisibility(isPointerOver: IsMouseOver);

        FloatingPracticePanel.Load(_practiceSession, _settings, _audioPlayback);
        PracticeModeChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ExitPractice()
    {
        StopAndResetAudio();
        if (!IsPracticeMode)
            return;

        FloatingPracticePanel.Clear();
        _practiceSession = null;
        IsPracticeMode = false;

        PreviousButton.Width = double.NaN;
        PreviousButton.Height = double.NaN;
        NextButton.Width = double.NaN;
        NextButton.Height = double.NaN;

        SentenceBox.Visibility = Visibility.Visible;
        NormalSentenceContainer.Visibility = Visibility.Visible;
        PracticeContainer.Visibility = Visibility.Collapsed;
        VocabularyPanel.Visibility = _showVocabularyCards ? Visibility.Visible : Visibility.Collapsed;

        ApplySettings(_settings);

        var activeList = StudyListRules.ActiveList(_settings);
        _hasMultipleSentences = activeList.Sentences.Count > 1;
        PreviousButton.IsEnabled = _hasMultipleSentences;
        NextButton.IsEnabled = _hasMultipleSentences;
        UpdateNavigationVisibility(isPointerOver: IsMouseOver);

        if (PracticeMenuItem is not null)
        {
            PracticeMenuItem.Header = "Start Practice";
        }

        PracticeModeChanged?.Invoke(this, EventArgs.Empty);
    }

    private void PracticeMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (IsPracticeMode)
        {
            ExitPractice();
        }
        else
        {
            StartPractice();
        }
    }

    private void PracticeExitButton_Click(object sender, RoutedEventArgs e)
    {
        ExitPractice();
    }

    private void Window_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e) => UpdateNavigationVisibility(isPointerOver: true);

    private void Window_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e) => UpdateNavigationVisibility(isPointerOver: false);

    private void PreviousButton_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        StopAndResetAudio();
        if (IsPracticeMode && _practiceSession is not null)
        {
            FloatingPracticePanel.MovePreviousSentence();
            return;
        }

        PreviousRequested?.Invoke(this, EventArgs.Empty);
    }

    private void NextButton_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        StopAndResetAudio();
        if (IsPracticeMode && _practiceSession is not null)
        {
            FloatingPracticePanel.MoveNextSentence();
            return;
        }

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
        for (var current = originalSource as DependencyObject; current is not null; current = GetAnyParent(current))
        {
            if (ReferenceEquals(current, button))
                return true;
        }

        return false;
    }

    private static bool IsWithinElementTree(object originalSource, FrameworkElement element)
    {
        for (var current = originalSource as DependencyObject; current is not null; current = GetAnyParent(current))
        {
            if (ReferenceEquals(current, element))
                return true;
        }

        return false;
    }

    private static DependencyObject? GetAnyParent(DependencyObject element)
    {
        if (element is Visual or System.Windows.Media.Media3D.Visual3D)
        {
            return VisualTreeHelper.GetParent(element) ?? (element as FrameworkElement)?.Parent;
        }

        if (element is FrameworkContentElement fce)
        {
            return fce.Parent;
        }

        return null;
    }

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

    protected override void OnClosed(EventArgs e)
    {
        IsVisibleChanged -= FloatingWindow_IsVisibleChanged;
        FloatingPracticePanel.Clear();
        _audioPlayback.StateChanged -= AudioPlayback_StateChanged;
        _audioPlayback.Dispose();
        ClearSentenceDocumentInlines();
        base.OnClosed(e);
    }

    private void ClearSentenceDocumentInlines()
    {
        foreach (var block in SentenceDocument.Blocks)
        {
            if (block is Paragraph paragraph)
            {
                foreach (var inline in paragraph.Inlines)
                {
                    if (inline is Span span)
                    {
                        span.MouseEnter -= OnVocabularySpanMouseEnter;
                        span.MouseLeave -= OnVocabularySpanMouseLeave;
                        span.QueryCursor -= OnVocabularySpanQueryCursor;
                        foreach (var child in span.Inlines)
                        {
                            child.QueryCursor -= OnVocabularySpanQueryCursor;
                        }
                    }
                }
            }
        }
    }

    private void SentenceBox_QueryCursor(object sender, QueryCursorEventArgs e)
    {
        var mousePos = Mouse.GetPosition(SentenceBox);
        var pointer = SentenceBox.GetPositionFromPoint(mousePos, snapToText: false);
        if (pointer is not null && FindVocabularyItemId(pointer.Parent as TextElement).HasValue)
        {
            e.Cursor = Cursors.Hand;
            e.Handled = true;
        }
    }

    private static void OnVocabularySpanQueryCursor(object sender, QueryCursorEventArgs e)
    {
        e.Cursor = Cursors.Hand;
        e.Handled = true;
    }

    private static Color GetHoverColor(Color baseColor)
    {
        var r = (byte)Math.Min(255, baseColor.R + ((255 - baseColor.R) * 2 / 3));
        var g = (byte)Math.Min(255, baseColor.G + ((255 - baseColor.G) * 2 / 3));
        var b = (byte)Math.Min(255, baseColor.B + ((255 - baseColor.B) * 2 / 3));
        return Color.FromRgb(r, g, b);
    }

    private static void OnVocabularySpanMouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (sender is Span span)
        {
            if (span.Resources["HoverBrush"] is Brush hoverBrush)
            {
                span.Foreground = hoverBrush;
            }

            if (span.Resources["HoverBackground"] is Brush hoverBg)
            {
                span.Background = hoverBg;
            }
        }
    }

    private static void OnVocabularySpanMouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (sender is Span span)
        {
            if (span.Resources["OriginalBrush"] is Brush originalBrush)
            {
                span.Foreground = originalBrush;
            }

            span.Background = System.Windows.Media.Brushes.Transparent;
        }
    }

    private void FloatingWindow_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (!IsVisible)
        {
            StopAndResetAudio();
        }
    }

    private async void AudioButton_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        await ToggleAudioPlaybackAsync();
    }

    private async Task ToggleAudioPlaybackAsync()
    {
        if (string.IsNullOrWhiteSpace(_currentSentenceText))
        {
            return;
        }

        var voice = string.IsNullOrWhiteSpace(_settings.TtsVoice) ? EdgeNeuralTtsService.DefaultVoice : _settings.TtsVoice;
        var speed = _settings.TtsSpeed is >= 0.8 and <= 1.2 ? _settings.TtsSpeed : 1.0;
        await _audioPlayback.ToggleAsync(_currentSentenceText, voice, speed);
    }

    public void StopAndResetAudio()
    {
        _audioPlayback.Stop();
    }

    private ISentenceAudioPlayback CreateAudioPlayback()
    {
        var playback = new SentenceAudioPlayback(_audioPlayer, _ttsService, _audioCacheManager);
        playback.StateChanged += AudioPlayback_StateChanged;
        return playback;
    }

    private void RecreateAudioPlayback(bool playerReplaced = false)
    {
        var previous = _audioPlayback;
        previous.StateChanged -= AudioPlayback_StateChanged;
        if (playerReplaced)
            previous.Dispose();
        else
            previous.Stop();
        _audioPlayback = CreateAudioPlayback();
        SetAudioButtonIdle();
        if (IsPracticeMode && _practiceSession is not null)
            FloatingPracticePanel.Load(_practiceSession, _settings, _audioPlayback);
    }

    private void AudioPlayback_StateChanged(AudioPlaybackState state)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.InvokeAsync(() => AudioPlayback_StateChanged(state));
            return;
        }

        state = _audioPlayback.State;
        if (state.IsLoading || state.IsPlaying)
            SetAudioButtonActive(state.IsLoading);
        else
        {
            SetAudioButtonIdle();
            if (state.Error is not null && AudioButton is not null)
                AudioButton.ToolTip = state.Error;
        }
    }

    private void SetAudioButtonIdle()
    {
        if (AudioButton is null)
            return;

        AudioButton.Content = AudioGlyphIdle;
        AudioButton.Foreground = AudioBrushIdle;
        AudioButton.ToolTip = "Play audio (TTS)";
    }

    private void SetAudioButtonActive(bool isLoading)
    {
        if (AudioButton is null)
            return;

        AudioButton.Content = AudioGlyphActive;
        AudioButton.Foreground = AudioBrushActive;
        AudioButton.ToolTip = isLoading ? "Loading audio..." : "Stop audio";
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
    public Brush AccentBrush { get; init; } = new SolidColorBrush(Color.FromRgb(0x5E, 0xEA, 0xD4));
    public Brush BorderBrush { get; init; } = new SolidColorBrush(Color.FromArgb(0x44, 0x38, 0xBD, 0xF8));
    public Thickness CardBorderThickness { get; init; } = new Thickness(1);

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
