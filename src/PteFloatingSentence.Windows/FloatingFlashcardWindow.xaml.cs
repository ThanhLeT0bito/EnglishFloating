using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using PteFloatingSentence.Core;
using PteFloatingSentence.Windows.Infrastructure;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;

namespace PteFloatingSentence.Windows;

public partial class FloatingFlashcardWindow : Window
{
    private readonly ISentenceAudioPlayback _audio;
    private IReadOnlyList<FlashcardItem> _cards = [];
    private int _currentIndex;
    private bool _isShowingBack;
    private int _cycleCount;
    private string _deckName = string.Empty;
    private string _voice = EdgeNeuralTtsService.DefaultVoice;
    private double _speed = 1.0;

    public event EventHandler? SettingsRequested;
    public event EventHandler? CloseRequested;
    public event EventHandler<(string CardKey, FlashcardRating Rating)>? CardRated;
    public event EventHandler<string>? CardMarkedDoneRequested;

    public FloatingFlashcardWindow() : this(new SentenceAudioPlayback(new WpfAudioPlayer(), new EdgeNeuralTtsService(), new AudioCacheManager())) { }

    public FloatingFlashcardWindow(ISentenceAudioPlayback audio)
    {
        _audio = audio ?? throw new ArgumentNullException(nameof(audio));
        InitializeComponent();
        _audio.StateChanged += Audio_StateChanged;
    }

    public int CurrentIndex => _currentIndex;
    public bool IsShowingBack => _isShowingBack;
    public int CycleCount => _cycleCount;
    public int CardCount => _cards.Count;
    public FlashcardItem? CurrentCard => _cards.Count > 0 && _currentIndex >= 0 && _currentIndex < _cards.Count ? _cards[_currentIndex] : null;

    public FrameworkElement FrontPanel => FrontFacePanel;
    public FrameworkElement BackPanel => BackFacePanel;
    public System.Windows.Controls.Button ButtonAgain => AgainButton;
    public System.Windows.Controls.Button ButtonHard => HardButton;
    public System.Windows.Controls.Button ButtonRemembered => RememberedButton;
    public System.Windows.Controls.Button ButtonMarkDone => MarkDoneButton;
    public System.Windows.Controls.Button ButtonAudio => AudioButton;

    public void SetAudioSettings(string? voice, double speed)
    {
        _voice = string.IsNullOrWhiteSpace(voice) ? EdgeNeuralTtsService.DefaultVoice : voice;
        _speed = speed is >= 0.8 and <= 1.2 ? speed : 1.0;
    }

    public void SetDeck(string deckName, IReadOnlyList<FlashcardItem> cards, int initialIndex = 0)
    {
        _deckName = deckName;
        _cards = cards;
        _currentIndex = cards.Count > 0 ? Math.Clamp(initialIndex, 0, cards.Count - 1) : 0;
        _isShowingBack = false;
        UpdateCardDisplay();
    }

    public void Flip()
    {
        if (_cards.Count == 0)
            return;

        _isShowingBack = !_isShowingBack;
        UpdateFaceVisibility();
    }

    public void NavigateNext()
    {
        if (_cards.Count == 0)
            return;

        _currentIndex = (_currentIndex + 1) % _cards.Count;
        _isShowingBack = false;
        UpdateCardDisplay();
    }

    public void NavigatePrevious()
    {
        if (_cards.Count == 0)
            return;

        _currentIndex = (_currentIndex - 1 + _cards.Count) % _cards.Count;
        _isShowingBack = false;
        UpdateCardDisplay();
    }

    public void SubmitRating(FlashcardRating rating)
    {
        if (!_isShowingBack || CurrentCard is null)
            return;

        var cardKey = CurrentCard.CardKey;
        var wasLastCard = _currentIndex == _cards.Count - 1;

        CardRated?.Invoke(this, (cardKey, rating));

        if (wasLastCard)
        {
            _cycleCount++;
            _currentIndex = 0;
        }
        else
        {
            _currentIndex++;
        }

        _isShowingBack = false;
        UpdateCardDisplay();
    }

    private void UpdateCardDisplay()
    {
        DeckNameText.Text = _deckName;
        _audio.Stop();
        SetAudioButtonIdle();

        if (_cards.Count == 0)
        {
            FrontPhraseText.Text = "No ready cards in deck";
            FrontPronunciationText.Text = string.Empty;
            ProgressText.Text = "0 / 0";
            StateBadge.Visibility = Visibility.Collapsed;
            AudioButton.IsEnabled = false;
            UpdateFaceVisibility();
            return;
        }

        var card = _cards[_currentIndex];
        AudioButton.IsEnabled = !string.IsNullOrWhiteSpace(card.Phrase);
        FrontPhraseText.Text = card.Phrase;
        FrontPronunciationText.Text = card.PronunciationIpa ?? string.Empty;
        FrontPronunciationText.Visibility = string.IsNullOrWhiteSpace(card.PronunciationIpa) ? Visibility.Collapsed : Visibility.Visible;

        BackPhraseText.Text = card.Phrase;
        BackPronunciationText.Text = card.PronunciationIpa ?? string.Empty;
        BackPronunciationText.Visibility = string.IsNullOrWhiteSpace(card.PronunciationIpa) ? Visibility.Collapsed : Visibility.Visible;
        BackMeaningText.Text = card.Meaning;
        BackExampleText.Text = card.Example;

        if (card.SourceSentences.Count > 0)
        {
            BackSourceText.Text = $"Source: {card.SourceSentences[0].Text}";
            BackSourceText.Visibility = Visibility.Visible;
        }
        else
        {
            BackSourceText.Visibility = Visibility.Collapsed;
        }

        var cycleInfo = _cycleCount > 0 ? $" • Cycle {_cycleCount}" : string.Empty;
        ProgressText.Text = $"{_currentIndex + 1} / {_cards.Count}{cycleInfo}";

        StateBadge.Visibility = Visibility.Visible;
        StateBadgeText.Text = card.State switch
        {
            FlashcardLearningState.Remembered => "Remembered",
            FlashcardLearningState.Learning => "Learning",
            _ => "New"
        };
        StateBadgeText.Foreground = card.State switch
        {
            FlashcardLearningState.Remembered => (SolidColorBrush)FindResource("RememberedBrush"),
            FlashcardLearningState.Learning => (SolidColorBrush)FindResource("HardBrush"),
            _ => new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x38, 0xBD, 0xF8))
        };

        UpdateFaceVisibility();
    }

    private void UpdateFaceVisibility()
    {
        FrontFacePanel.Visibility = _isShowingBack ? Visibility.Collapsed : Visibility.Visible;
        BackFacePanel.Visibility = _isShowingBack ? Visibility.Visible : Visibility.Collapsed;

        // Ratings are only enabled when the back of the card is visible
        var canRate = _isShowingBack && _cards.Count > 0;
        AgainButton.IsEnabled = canRate;
        HardButton.IsEnabled = canRate;
        RememberedButton.IsEnabled = canRate;
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (IsWithinButtonTree(e.OriginalSource as DependencyObject, MarkDoneButton) ||
            IsWithinButtonTree(e.OriginalSource as DependencyObject, SettingsButton) ||
            IsWithinButtonTree(e.OriginalSource as DependencyObject, CloseButton))
        {
            return;
        }

        if (e.ButtonState == MouseButtonState.Pressed)
        {
            try
            {
                DragMove();
            }
            catch (InvalidOperationException)
            {
            }
        }
    }

    private static bool IsWithinButtonTree(DependencyObject? source, DependencyObject? target)
    {
        if (source is null || target is null)
            return false;

        var current = source;
        while (current is not null)
        {
            if (ReferenceEquals(current, target))
                return true;

            current = VisualTreeHelper.GetParent(current);
        }

        return false;
    }

    private void CardContentArea_MouseLeftButtonUp(object sender, MouseButtonEventArgs e) => Flip();

    private void PreviousButton_Click(object sender, RoutedEventArgs e) => NavigatePrevious();

    private void NextButton_Click(object sender, RoutedEventArgs e) => NavigateNext();

    private void AgainButton_Click(object sender, RoutedEventArgs e) => SubmitRating(FlashcardRating.Again);

    private void HardButton_Click(object sender, RoutedEventArgs e) => SubmitRating(FlashcardRating.Hard);

    private void RememberedButton_Click(object sender, RoutedEventArgs e) => SubmitRating(FlashcardRating.Remembered);

    public void MarkCurrentCardDone()
    {
        if (CurrentCard is null)
            return;

        var cardKey = CurrentCard.CardKey;
        CardMarkedDoneRequested?.Invoke(this, cardKey);
    }

    private void MarkDoneButton_Click(object sender, RoutedEventArgs e) => MarkCurrentCardDone();

    private void SettingsButton_Click(object sender, RoutedEventArgs e) =>
        SettingsRequested?.Invoke(this, EventArgs.Empty);

    private void AudioButton_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        _ = ToggleAudioPlaybackAsync();
    }

    public async Task ToggleAudioPlaybackAsync()
    {
        if (CurrentCard is null || string.IsNullOrWhiteSpace(CurrentCard.Phrase))
            return;

        await _audio.ToggleAsync(CurrentCard.Phrase, _voice, _speed);
    }

    private void Audio_StateChanged(AudioPlaybackState state)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.InvokeAsync(() => Audio_StateChanged(state));
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
        if (AudioButtonIcon is null || AudioButton is null) return;
        AudioButtonIcon.Text = "\uE767";
        AudioButton.Foreground = (Brush?)FindResource("TextMutedBrush") ?? Brushes.Gray;
        AudioButton.ToolTip = "Listen pronunciation (A)";
    }

    private void SetAudioButtonActive(bool isLoading)
    {
        if (AudioButtonIcon is null || AudioButton is null) return;
        AudioButtonIcon.Text = "\uE768";
        AudioButton.Foreground = (Brush?)FindResource("AccentBrush") ?? Brushes.LightSkyBlue;
        AudioButton.ToolTip = isLoading ? "Loading audio..." : "Stop audio (A)";
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        _audio.Stop();
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    private void Window_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Space:
                Flip();
                e.Handled = true;
                break;
            case Key.Left:
                NavigatePrevious();
                e.Handled = true;
                break;
            case Key.Right:
                NavigateNext();
                e.Handled = true;
                break;
            case Key.A:
                _ = ToggleAudioPlaybackAsync();
                e.Handled = true;
                break;
            case Key.D:
                MarkCurrentCardDone();
                e.Handled = true;
                break;
            case Key.D1:
            case Key.NumPad1:
                if (_isShowingBack)
                {
                    SubmitRating(FlashcardRating.Again);
                    e.Handled = true;
                }
                break;
            case Key.D2:
            case Key.NumPad2:
                if (_isShowingBack)
                {
                    SubmitRating(FlashcardRating.Hard);
                    e.Handled = true;
                }
                break;
            case Key.D3:
            case Key.NumPad3:
                if (_isShowingBack)
                {
                    SubmitRating(FlashcardRating.Remembered);
                    e.Handled = true;
                }
                break;
            case Key.Escape:
                CloseRequested?.Invoke(this, EventArgs.Empty);
                e.Handled = true;
                break;
        }
    }

    public void ResetState()
    {
        _cycleCount = 0;
        _currentIndex = 0;
        _isShowingBack = false;
        UpdateCardDisplay();
    }

    protected override void OnClosed(EventArgs e)
    {
        _audio.Stop();
        _audio.StateChanged -= Audio_StateChanged;
        _audio.Dispose();
        base.OnClosed(e);
    }
}
