using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using PteFloatingSentence.Core;
using PteFloatingSentence.Windows.Infrastructure;
using UserControl = System.Windows.Controls.UserControl;
using TextBox = System.Windows.Controls.TextBox;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using KeyEventHandler = System.Windows.Input.KeyEventHandler;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using HorizontalAlignment = System.Windows.HorizontalAlignment;

namespace PteFloatingSentence.Windows;

public partial class ReviewPracticePage : UserControl, IDisposable
{
    private static readonly char[] TrimPunctuationChars = [
        '.', ',', '!', '?', ';', ':', '"', '\'', '(', ')', '[', ']', '{', '}', '-', '—'
    ];

    private AppSettings? _settings;
    private Action<Guid, Guid, bool>? _onSentenceCompleted;
    private ReviewPracticeSession? _session;
    private readonly ISentenceAudioPlayback _audio;
    private int? _seed;
    private bool _isDisposed;

    private readonly KeyEventHandler _keyDownHandler;
    private readonly RoutedEventHandler _gotFocusHandler;
    private readonly RoutedEventHandler _lostFocusHandler;

    public ReviewPracticePage() : this(new SentenceAudioPlayback(new WpfAudioPlayer(), new EdgeNeuralTtsService(), new AudioCacheManager())) { }

    public ReviewPracticePage(ISentenceAudioPlayback audio)
    {
        _audio = audio ?? throw new ArgumentNullException(nameof(audio));
        InitializeComponent();

        _keyDownHandler = OnSentencePanelKeyDown;
        _gotFocusHandler = OnSentencePanelGotFocus;
        _lostFocusHandler = OnSentencePanelLostFocus;

        SentenceProjectionPanel.AddHandler(UIElement.KeyDownEvent, _keyDownHandler);
        SentenceProjectionPanel.AddHandler(UIElement.GotFocusEvent, _gotFocusHandler);
        SentenceProjectionPanel.AddHandler(UIElement.LostFocusEvent, _lostFocusHandler);

        _audio.StateChanged += AudioPlayback_StateChanged;
        Unloaded += OnPageUnloaded;
        IsVisibleChanged += OnVisibilityChanged;
    }

    public void Initialize(AppSettings settings, Action<Guid, Guid, bool>? onSentenceCompleted = null, int? seed = null)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _onSentenceCompleted = onSentenceCompleted;
        _seed = seed;

        PopulateListSelector();
    }

    public void LoadList(StudyList list)
    {
        ArgumentNullException.ThrowIfNull(list);
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        _audio.Stop();
        SetAudioButtonIdle();

        _session = new ReviewPracticeSession(list, _seed, (sentenceId, completed) =>
        {
            _onSentenceCompleted?.Invoke(list.Id, sentenceId, completed);
        });

        RenderSession();
    }

    private void PopulateListSelector()
    {
        if (_settings is null) return;

        StudyListSelector.Items.Clear();
        var selectedIdx = 0;
        for (var i = 0; i < _settings.StudyLists.Count; i++)
        {
            var list = _settings.StudyLists[i];
            StudyListSelector.Items.Add(new ComboBoxItem
            {
                Content = list.Name,
                Tag = list.Id
            });

            if (list.Id == _settings.ActiveListId)
            {
                selectedIdx = i;
            }
        }

        if (_settings.StudyLists.Count > 0)
        {
            StudyListSelector.SelectedIndex = selectedIdx;
        }
        else
        {
            ShowEmptyState();
        }
    }

    private void StudyListSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_settings is null || StudyListSelector.SelectedItem is not ComboBoxItem item)
        {
            return;
        }

        if (item.Tag is Guid listId)
        {
            var list = _settings.StudyLists.FirstOrDefault(l => l.Id == listId);
            if (list is not null)
            {
                LoadList(list);
            }
        }
    }

    private void RenderSession()
    {
        InlineErrorLabel.Visibility = Visibility.Collapsed;
        RevealedAnswerLabel.Visibility = Visibility.Collapsed;

        if (_session is null || _session.List.Sentences.Count == 0)
        {
            ShowEmptyState();
            return;
        }

        ReviewEmptyStateLabel.Visibility = Visibility.Collapsed;
        PracticeArea.Visibility = Visibility.Visible;

        ProgressLabel.Text = $"Sentence {_session.SentenceIndex + 1} of {_session.List.Sentences.Count}";

        SetAudioButtonIdle();
        var currentText = _session.CurrentReview?.OriginalText;
        if (AudioButton is not null)
        {
            AudioButton.IsEnabled = !string.IsNullOrWhiteSpace(currentText);
        }

        BuildSentenceProjection();

        if (_session.IsComplete)
        {
            ShowCompletionBanner();
        }
        else
        {
            CompletionPanel.Visibility = Visibility.Collapsed;
            FocusActiveHiddenBox();
        }
    }

    private void BuildSentenceProjection()
    {
        SentenceProjectionPanel.Children.Clear();
        if (_session is null) return;

        var review = _session.CurrentReview;
        var textBrush = (Brush?)FindResource("TextPrimaryBrush") ?? Brushes.White;
        var inputBgBrush = new SolidColorBrush(Color.FromRgb(30, 41, 59));
        var inputBorderBrush = (Brush?)FindResource("InputBorderBrush") ?? Brushes.Gray;

        for (var i = 0; i < review.Tokens.Count; i++)
        {
            var token = review.Tokens[i];
            if (!token.IsHidden)
            {
                var textBlock = new TextBlock
                {
                    Text = token.SourceText,
                    FontSize = 16,
                    Foreground = textBrush,
                    Margin = new Thickness(0, 4, 8, 4),
                    VerticalAlignment = VerticalAlignment.Center
                };
                SentenceProjectionPanel.Children.Add(textBlock);
            }
            else
            {
                var hiddenPos = review.HiddenTokenIndexes.ToList().IndexOf(i);
                var isPastAnswered = hiddenPos < _session.CurrentHiddenPosition || _session.IsComplete;

                var textBox = new TextBox
                {
                    Tag = hiddenPos,
                    FontSize = 15,
                    Width = Math.Max(50, (token.SourceText.Length * 12) + 16),
                    Padding = new Thickness(4, 2, 4, 2),
                    Margin = new Thickness(0, 2, 8, 2),
                    Background = inputBgBrush,
                    Foreground = textBrush,
                    BorderBrush = inputBorderBrush,
                    HorizontalContentAlignment = HorizontalAlignment.Center,
                    VerticalContentAlignment = VerticalAlignment.Center,
                    Text = isPastAnswered ? token.SourceText : "_"
                };

                if (isPastAnswered)
                {
                    textBox.IsEnabled = false;
                    textBox.Foreground = new SolidColorBrush(Color.FromRgb(52, 211, 153)); // Emerald green
                }

                SentenceProjectionPanel.Children.Add(textBox);
            }
        }
    }

    private void ShowEmptyState()
    {
        ProgressLabel.Text = "No sentences";
        ReviewEmptyStateLabel.Visibility = Visibility.Visible;
        PracticeArea.Visibility = Visibility.Collapsed;
        SentenceProjectionPanel.Children.Clear();
        if (AudioButton is not null)
        {
            AudioButton.IsEnabled = false;
            SetAudioButtonIdle();
        }
    }

    private void ShowCompletionBanner()
    {
        CompletionPanel.Visibility = Visibility.Visible;
        if (_session is not null && _session.IsAllSentencesCompleted)
        {
            CompletionStatusLabel.Text = "All sentences in this list completed!";
            CompletionSubtextLabel.Text = "Great job! You have mastered all sentences in this list.";
        }
        else
        {
            CompletionStatusLabel.Text = "Sentence completed!";
            CompletionSubtextLabel.Text = "All hidden words answered correctly.";
        }
    }

    private void FocusActiveHiddenBox()
    {
        if (_session is null || _session.IsComplete) return;

        foreach (var child in SentenceProjectionPanel.Children)
        {
            if (child is TextBox box && box.Tag is int pos && pos == _session.CurrentHiddenPosition)
            {
                box.Focus();
                if (box.Text == "_")
                {
                    box.Text = string.Empty;
                }
                box.SelectAll();
                break;
            }
        }
    }

    private void OnSentencePanelKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && e.OriginalSource is TextBox box)
        {
            e.Handled = true;
            SubmitBoxAnswer(box);
        }
    }

    private void OnSentencePanelGotFocus(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is TextBox box && box.Text == "_")
        {
            box.Text = string.Empty;
        }
    }

    private void OnSentencePanelLostFocus(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is TextBox box && string.IsNullOrEmpty(box.Text))
        {
            box.Text = "_";
        }
    }

    private void CheckAnswerButton_Click(object sender, RoutedEventArgs e)
    {
        if (_session is null || _session.IsComplete) return;

        // Find the currently active hidden textbox
        foreach (var child in SentenceProjectionPanel.Children)
        {
            if (child is TextBox box && box.Tag is int pos && pos == _session.CurrentHiddenPosition)
            {
                SubmitBoxAnswer(box);
                return;
            }
        }
    }

    private void SubmitBoxAnswer(TextBox box)
    {
        if (_session is null || _session.IsComplete) return;

        var answer = box.Text == "_" ? string.Empty : box.Text;
        var result = _session.Submit(answer);

        if (result.IsCorrect)
        {
            InlineErrorLabel.Visibility = Visibility.Collapsed;
            RevealedAnswerLabel.Visibility = Visibility.Collapsed;

            // Display revealed correct word on the box
            var tokenIdx = _session.CurrentReview.HiddenTokenIndexes[(int)box.Tag];
            box.Text = _session.CurrentReview.Tokens[tokenIdx].SourceText;
            box.IsEnabled = false;
            box.Foreground = new SolidColorBrush(Color.FromRgb(52, 211, 153));

            if (result.IsComplete)
            {
                ShowCompletionBanner();
            }
            else
            {
                FocusActiveHiddenBox();
            }
        }
        else
        {
            InlineErrorLabel.Text = result.Error ?? "Try again.";
            InlineErrorLabel.Visibility = Visibility.Visible;
            box.Focus();
            box.SelectAll();
        }
    }

    private void ShowAnswerButton_Click(object sender, RoutedEventArgs e)
    {
        if (_session is null || _session.IsComplete) return;

        if (_session.CurrentHiddenPosition < _session.CurrentReview.HiddenTokenIndexes.Count)
        {
            var tokenIdx = _session.CurrentReview.HiddenTokenIndexes[_session.CurrentHiddenPosition];
            var expected = _session.CurrentReview.Tokens[tokenIdx].SourceText.Trim().Trim(TrimPunctuationChars);
            RevealedAnswerLabel.Text = $"Answer: {expected}";
            RevealedAnswerLabel.Visibility = Visibility.Visible;
        }
    }

    private void PreviousSentenceButton_Click(object sender, RoutedEventArgs e)
    {
        if (_session is null) return;
        _audio.Stop();
        _session.MovePreviousSentence();
        RenderSession();
    }

    private void NextSentenceButton_Click(object sender, RoutedEventArgs e)
    {
        if (_session is null) return;
        _audio.Stop();
        _session.MoveNextSentence();
        RenderSession();
    }

    private void RestartListButton_Click(object sender, RoutedEventArgs e)
    {
        if (_session is null) return;
        _audio.Stop();
        _session.RestartList();
        RenderSession();
    }

    private async void AudioButton_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (_session is null) return;
        var text = _session.CurrentReview?.OriginalText;
        if (string.IsNullOrWhiteSpace(text)) return;

        var voice = string.IsNullOrWhiteSpace(_settings?.TtsVoice) ? EdgeNeuralTtsService.DefaultVoice : _settings.TtsVoice;
        var speed = _settings?.TtsSpeed is >= 0.8 and <= 1.2 ? _settings.TtsSpeed : 1.0;
        await _audio.ToggleAsync(text, voice, speed);
    }

    private void AudioPlayback_StateChanged(AudioPlaybackState state)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.InvokeAsync(() => AudioPlayback_StateChanged(state));
            return;
        }

        if (state.IsLoading || state.IsPlaying)
        {
            SetAudioButtonActive(state.IsLoading);
        }
        else
        {
            SetAudioButtonIdle();
            if (state.Error is not null && AudioButton is not null)
            {
                AudioButton.ToolTip = state.Error;
            }
        }
    }

    private void SetAudioButtonIdle()
    {
        if (AudioButton is null) return;
        AudioButton.Content = "\uE767";
        AudioButton.Foreground = (Brush?)FindResource("TextMutedBrush") ?? Brushes.Gray;
        AudioButton.ToolTip = "Play audio (TTS)";
    }

    private void SetAudioButtonActive(bool isLoading)
    {
        if (AudioButton is null) return;
        AudioButton.Content = "\uE768";
        AudioButton.Foreground = (Brush?)FindResource("AccentBrush") ?? Brushes.LightSkyBlue;
        AudioButton.ToolTip = isLoading ? "Loading audio..." : "Stop audio";
    }

    private void OnVisibilityChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (!IsVisible)
        {
            _audio.Stop();
            SetAudioButtonIdle();
        }
    }

    private void OnPageUnloaded(object sender, RoutedEventArgs e)
    {
        Dispose();
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        Unloaded -= OnPageUnloaded;
        IsVisibleChanged -= OnVisibilityChanged;
        SentenceProjectionPanel.RemoveHandler(UIElement.KeyDownEvent, _keyDownHandler);
        SentenceProjectionPanel.RemoveHandler(UIElement.GotFocusEvent, _gotFocusHandler);
        SentenceProjectionPanel.RemoveHandler(UIElement.LostFocusEvent, _lostFocusHandler);

        _audio.StateChanged -= AudioPlayback_StateChanged;
        _audio.Dispose();
    }
}
