using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PteFloatingSentence.Core;
using PteFloatingSentence.Windows.Infrastructure;
using UserControl = System.Windows.Controls.UserControl;
using TextBox = System.Windows.Controls.TextBox;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace PteFloatingSentence.Windows;

public partial class PracticePanel : UserControl
{
    private ReviewPracticeSession? _session;
    private AppSettings _settings = new();
    private ISentenceAudioPlayback? _audio;
    private Action<AudioPlaybackState>? _audioHandler;
    private long _audioGeneration;

    public event EventHandler? SentenceChanged;

    public static readonly DependencyProperty CompactProperty = DependencyProperty.Register(
        nameof(Compact), typeof(bool), typeof(PracticePanel),
        new PropertyMetadata(false, (owner, _) => ((PracticePanel)owner).ApplySizing()));

    public bool Compact
    {
        get => (bool)GetValue(CompactProperty);
        set => SetValue(CompactProperty, value);
    }

    public PracticePanel()
    {
        InitializeComponent();
        Loaded += (_, _) => AttachAudio();
        Unloaded += (_, _) => { DetachAudio(); ResetInteraction(); };
        Render();
    }

    public void Load(ReviewPracticeSession session, AppSettings settings, ISentenceAudioPlayback audio)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(audio);
        DetachAudio();
        // The previous workflow belongs to the host; stop it without disposing it.
        if (_audio is not null && !ReferenceEquals(_audio, audio)) _audio.Stop();
        _session = session;
        _settings = settings;
        _audio = audio;
        ResetInteraction();
        AttachAudio();
        Render();
    }

    public void Clear()
    {
        DetachAudio();
        ResetInteraction();
        _audio = null;
        _session = null;
        Render();
    }

    public void UpdateMode(PracticeMode mode)
    {
        _settings = _settings with { PracticeMode = mode };
        ResetInteraction();
        Render();
    }

    private void AttachAudio()
    {
        if (_audio is null || _audioHandler is not null) return;
        var source = _audio;
        _audioHandler = _ =>
        {
            var generation = _audioGeneration;
            void Update()
            {
                if (_audioHandler is not null && ReferenceEquals(source, _audio) && generation == _audioGeneration)
                    RenderAudio(source.State);
            }
            if (Dispatcher.CheckAccess()) Update();
            else Dispatcher.BeginInvoke((Action)Update);
        };
        source.StateChanged += _audioHandler;
        RenderAudio(source.State);
    }

    private void DetachAudio()
    {
        _audioGeneration++;
        if (_audio is not null && _audioHandler is not null) _audio.StateChanged -= _audioHandler;
        _audioHandler = null;
    }

    private void ResetInteraction()
    {
        _audioGeneration++;
        _audio?.Stop();
        DictationInput.Clear();
        foreach (var box in PracticeProjectionPanel.Children.OfType<TextBox>()) box.Clear();
        PracticeFeedbackLabel.Text = string.Empty;
        PracticeFeedbackLabel.Visibility = Visibility.Collapsed;
        RenderAudio(new(false, false, null));
    }

    private void RenderAudio(AudioPlaybackState state)
    {
        PracticeAudioButton.Content = state.IsLoading || state.IsPlaying ? "\uE71A" : "\uE767";
        var label = state.IsLoading ? "Cancel audio loading" : state.IsPlaying ? "Stop audio" : "Play audio";
        PracticeAudioButton.ToolTip = label;
        System.Windows.Automation.AutomationProperties.SetName(PracticeAudioButton, label);
        if (state.Error is not null) ShowFeedback("Audio unavailable. Try again.");
        else if (PracticeFeedbackLabel.Text == "Audio unavailable. Try again.")
            PracticeFeedbackLabel.Visibility = Visibility.Collapsed;
    }

    private void Render()
    {
        PracticeProjectionPanel.Children.Clear();
        var hasSentence = _session is not null && _session.List.Sentences.Count > 0;
        PracticeAudioButton.IsEnabled = hasSentence;
        PracticePreviousButton.IsEnabled = PracticeNextButton.IsEnabled = hasSentence;
        PracticeRestartButton.IsEnabled = hasSentence;
        PracticeCheckButton.IsEnabled = hasSentence && !_session!.IsComplete;
        PracticeProgressLabel.Text = hasSentence
            ? $"Sentence {_session!.SentenceIndex + 1} of {_session.List.Sentences.Count}" : "No sentences";
        var dictation = _settings.PracticeMode == PracticeMode.ListenAndWrite;
        DictationInput.Visibility = hasSentence && dictation ? Visibility.Visible : Visibility.Collapsed;
        DictationInput.IsEnabled = hasSentence && !_session!.IsComplete;
        PracticeProjectionPanel.Visibility = dictation ? Visibility.Collapsed : Visibility.Visible;
        PracticeCompletionPanel.Visibility = hasSentence && _session!.IsComplete ? Visibility.Visible : Visibility.Collapsed;
        PracticeCompletionLabel.Text = _session?.IsAllSentencesCompleted == true
            ? "All sentences in this list completed!" : "Sentence completed!";
        if (hasSentence && !dictation)
        {
            var review = _session!.CurrentReview;
            for (var i = 0; i < review.Tokens.Count; i++)
            {
                var token = review.Tokens[i];
                var position = review.HiddenTokenIndexes.ToList().IndexOf(i);
                if (!token.IsHidden || position < _session.CurrentHiddenPosition || _session.IsComplete)
                    PracticeProjectionPanel.Children.Add(new TextBlock
                    { Text = token.SourceText, Margin = new Thickness(0, 4, 8, 4), VerticalAlignment = VerticalAlignment.Center });
                else
                {
                    var box = new TextBox
                    {
                        Tag = position, MinWidth = 60, Width = Math.Max(60, token.SourceText.Length * 12 + 16),
                        Margin = new Thickness(0, 2, 8, 2), IsEnabled = position == _session.CurrentHiddenPosition
                    };
                    System.Windows.Automation.AutomationProperties.SetName(box, "Hidden word");
                    box.PreviewKeyDown += HintInput_KeyDown;
                    PracticeProjectionPanel.Children.Add(box);
                }
            }
        }
        ApplySizing();
    }

    private void ApplySizing()
    {
        if (PracticeRoot is null) return;
        PracticeRoot.Margin = new Thickness(Compact ? 6 : 16);
        FontSize = Compact ? 14 : 16;
        PracticePreviousButton.Visibility = PracticeNextButton.Visibility = Compact ? Visibility.Collapsed : Visibility.Visible;
    }

    private async void PracticeAudioButton_Click(object sender, RoutedEventArgs e)
    {
        if (_session is null || _session.List.Sentences.Count == 0 || _audio is null) return;
        await _audio.ToggleAsync(_session.List.Sentences[_session.SentenceIndex].Text, _settings.TtsVoice, _settings.TtsSpeed);
    }

    private void DictationInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        Submit();
    }

    private void HintInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Enter or Key.Space)) return;
        e.Handled = true;
        Submit();
    }

    private void Check_Click(object sender, RoutedEventArgs e) => Submit();

    private void Submit()
    {
        if (_session is null || _session.IsComplete) return;
        var box = _settings.PracticeMode == PracticeMode.ListenAndWrite ? DictationInput
            : PracticeProjectionPanel.Children.OfType<TextBox>().FirstOrDefault(b => b.IsEnabled);
        if (box is null) return;
        var result = _settings.PracticeMode == PracticeMode.ListenAndWrite
            ? _session.SubmitDictation(box.Text) : _session.Submit(box.Text);
        if (!result.IsCorrect)
        {
            ShowFeedback("Try again.");
            box.Focus();
            box.SelectAll();
            return;
        }
        PracticeFeedbackLabel.Visibility = Visibility.Collapsed;
        if (result.IsComplete)
        {
            ResetInteraction();
            if (!_session.IsAllSentencesCompleted)
            {
                _session.MoveNextSentence();
                SentenceChanged?.Invoke(this, EventArgs.Empty);
            }
        }
        Render();
        if (_settings.PracticeMode == PracticeMode.ListenAndWrite) DictationInput.Focus();
        else PracticeProjectionPanel.Children.OfType<TextBox>().FirstOrDefault(b => b.IsEnabled)?.Focus();
    }

    private void ShowFeedback(string message)
    {
        PracticeFeedbackLabel.Text = message;
        PracticeFeedbackLabel.Visibility = Visibility.Visible;
    }

    private void Navigate(Action<ReviewPracticeSession> action)
    {
        if (_session is null) return;
        ResetInteraction();
        action(_session);
        Render();
        SentenceChanged?.Invoke(this, EventArgs.Empty);
    }

    public void MovePreviousSentence() => Navigate(s => s.MovePreviousSentence());
    public void MoveNextSentence() => Navigate(s => s.MoveNextSentence());
    private void Previous_Click(object sender, RoutedEventArgs e) => MovePreviousSentence();
    private void Next_Click(object sender, RoutedEventArgs e) => MoveNextSentence();
    private void Restart_Click(object sender, RoutedEventArgs e) => Navigate(s => s.RestartList());
}
