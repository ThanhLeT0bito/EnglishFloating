using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using PteFloatingSentence.Core;
using PteFloatingSentence.Windows.Infrastructure;

using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using SolidColorBrush = System.Windows.Media.SolidColorBrush;

namespace PteFloatingSentence.Windows;

/// <summary>
/// The vocabulary check: after studying, the learner types the word from its meaning or the meaning from the word.
/// Words are checked here (small typos get a second try); meanings are graded by the AI, or by the learner when the
/// AI is not available. Each answer updates the card's flashcard progress.
/// </summary>
public partial class FlashcardsPage
{
    /// <summary>Where a check question is.</summary>
    public enum CheckStage { Off, Loading, Answering, Grading, SelfGrade, Answered, Summary }

    private sealed record MissedWord(string Phrase, string Meaning);

    private IReadOnlyList<VocabularyCheckQuestion> _checkQuestions = [];
    private readonly List<VocabularyCheckQuestion> _checkMissed = [];
    private int _checkIndex;
    private int _checkCorrect;
    private bool _checkHadSecondTry;
    private bool _renderingCheckOptions;
    private string? _checkNotice;
    private CancellationTokenSource? _checkCts;

    /// <summary>Translates meanings and grades typed meanings; without it meanings stay in English and the learner grades.</summary>
    public IVocabularyChecker? VocabularyChecker { get; set; }

    public bool IsChecking => CheckStageNow != CheckStage.Off;
    public CheckStage CheckStageNow { get; private set; } = CheckStage.Off;
    public VocabularyCheckQuestion? CurrentCheckQuestion =>
        _checkIndex >= 0 && _checkIndex < _checkQuestions.Count ? _checkQuestions[_checkIndex] : null;
    public int CheckCorrectCount => _checkCorrect;
    public int CheckMissedCount => _checkMissed.Count;
    public string CheckFeedback => CheckFeedbackTitle.Text;

    private MeaningLanguage MeaningLanguage => MeaningLanguages.Find(_settings.FlashcardMeaningLanguage);

    #region Options

    private void RenderCheckOptions()
    {
        _renderingCheckOptions = true;
        try
        {
            if (MeaningLanguageCombo.ItemsSource is null)
                MeaningLanguageCombo.ItemsSource = MeaningLanguages.All;
            MeaningLanguageCombo.SelectedItem = MeaningLanguage;
            CheckDirectionCombo.SelectedIndex = (int)_settings.VocabularyCheckDirection;
        }
        finally
        {
            _renderingCheckOptions = false;
        }
    }

    private void MeaningLanguageCombo_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_renderingCheckOptions || MeaningLanguageCombo.SelectedItem is not MeaningLanguage language)
            return;
        SetMeaningLanguage(language.Code);
    }

    private void CheckDirectionCombo_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_renderingCheckOptions || CheckDirectionCombo.SelectedIndex < 0)
            return;
        SetCheckDirection((VocabularyCheckDirection)CheckDirectionCombo.SelectedIndex);
    }

    public void SetMeaningLanguage(string code)
    {
        var normalized = MeaningLanguages.Normalize(code);
        if (normalized == _settings.FlashcardMeaningLanguage)
            return;
        ChangeSettings(_settings with { FlashcardMeaningLanguage = normalized });
        RenderCheckOptions();
    }

    public void SetCheckDirection(VocabularyCheckDirection direction)
    {
        if (!Enum.IsDefined(direction) || direction == _settings.VocabularyCheckDirection)
            return;
        ChangeSettings(_settings with { VocabularyCheckDirection = direction });
        RenderCheckOptions();
    }

    private void ChangeSettings(AppSettings settings)
    {
        _settings = settings;
        _onSettingsChanged?.Invoke(_settings);
    }

    /// <summary>The meaning to show for <paramref name="card"/>: in the chosen language when it has been translated.</summary>
    private string MeaningFor(FlashcardItem card) =>
        VocabularyCheckRules.MeaningIn(_settings, card, _settings.FlashcardMeaningLanguage) ?? card.Meaning;

    /// <summary>Asks the AI once for the meanings of <paramref name="cards"/> the chosen language does not have yet.</summary>
    /// <returns>A message when they could not be translated, otherwise null.</returns>
    private async Task<string?> EnsureTranslationsAsync(IEnumerable<FlashcardItem> cards, CancellationToken cancellationToken)
    {
        var language = MeaningLanguage;
        var missing = VocabularyCheckRules.MissingTranslations(_settings, cards, language.Code);
        if (missing.Count == 0)
            return null;
        if (VocabularyChecker is null)
            return $"Meanings are shown in English: add an AI key in Settings → AI to get them in {language.Name}.";

        try
        {
            var texts = await VocabularyChecker.TranslateMeaningsAsync(
                missing.Select(card => new MeaningToTranslate(card.Phrase, card.Meaning)).ToList(), language, cancellationToken);
            if (cancellationToken.IsCancellationRequested)
                return null;
            ChangeSettings(VocabularyCheckRules.AddTranslations(_settings, language.Code, missing.Zip(texts, (card, text) => (card, text))));
            return null;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception error)
        {
            return $"Could not get the {language.Name} meanings ({error.Message}). Showing English.";
        }
    }

    #endregion

    #region Check flow

    private void CheckWordsButton_Click(object sender, RoutedEventArgs e) => _ = StartCheckAsync();

    private void CheckAgainButton_Click(object sender, RoutedEventArgs e) => _ = StartCheckAsync();

    private void CheckRetryMissedButton_Click(object sender, RoutedEventArgs e) =>
        _ = StartCheckAsync(_checkMissed.Select(question => question.Card).ToList());

    /// <summary>Starts a check of the selected deck, or of <paramref name="only"/> these cards.</summary>
    public async Task StartCheckAsync(IReadOnlyList<FlashcardItem>? only = null)
    {
        if (_selectedDeckKey is null)
            return;
        var pool = only ?? FlashcardDeckProjection.GetDeckCards(_settings, _selectedDeckKey);
        var questions = VocabularyCheckRules.PickQuestions(pool, _settings.VocabularyCheckDirection,
            only is null ? VocabularyCheckRules.DefaultQuestionCount : Math.Max(1, only.Count));
        if (questions.Count == 0)
            return;

        if (_isStudying)
            ExitInPageStudy();
        CancelCheck();
        var cts = new CancellationTokenSource();
        _checkCts = cts;

        _checkQuestions = questions;
        _checkMissed.Clear();
        _checkIndex = 0;
        _checkCorrect = 0;
        _checkNotice = null;
        CheckDeckTitleText.Text = $"Check · {SelectedDeckNameText.Text}";
        DeckManagementView.Visibility = Visibility.Collapsed;
        InPageStudyView.Visibility = Visibility.Collapsed;
        InPageCheckView.Visibility = Visibility.Visible;

        if (VocabularyCheckRules.MissingTranslations(_settings, questions.Select(question => question.Card), _settings.FlashcardMeaningLanguage).Count > 0)
        {
            SetCheckStage(CheckStage.Loading);
            CheckPromptLabel.Text = string.Empty;
            CheckPromptText.Text = $"Getting the {MeaningLanguage.Name} meanings…";
            CheckPromptSubText.Text = string.Empty;
            _checkNotice = await EnsureTranslationsAsync(questions.Select(question => question.Card), cts.Token);
            if (cts.IsCancellationRequested || !ReferenceEquals(_checkCts, cts))
                return;
        }

        ShowCheckQuestion();
    }

    private void ShowCheckQuestion()
    {
        var question = CurrentCheckQuestion;
        if (question is null)
        {
            ShowCheckSummary();
            return;
        }

        _audio.Stop();
        _checkHadSecondTry = false;
        var card = question.Card;
        if (question.Direction == VocabularyCheckDirection.MeaningToWord)
        {
            CheckPromptLabel.Text = "Which word means this?";
            CheckPromptText.Text = VocabularyCheckRules.MaskPhrase(MeaningFor(card), card.Phrase);
            var example = VocabularyCheckRules.MaskPhrase(card.Example, card.Phrase);
            CheckPromptSubText.Text = example.Length == 0 ? string.Empty : $"“{example}”";
            CheckAnswerPlaceholder.Text = "Type the word…";
        }
        else
        {
            CheckPromptLabel.Text = "What does it mean?";
            CheckPromptText.Text = card.Phrase;
            CheckPromptSubText.Text = card.PronunciationIpa ?? string.Empty;
            CheckAnswerPlaceholder.Text = MeaningLanguage.Code == MeaningLanguages.English
                ? "Type the meaning…"
                : $"Type the meaning in {MeaningLanguage.Name} or English…";
        }

        CheckNoticeText.Text = _checkNotice ?? string.Empty;
        CheckNoticeText.Visibility = string.IsNullOrEmpty(_checkNotice) ? Visibility.Collapsed : Visibility.Visible;
        CheckAnswerInput.Text = string.Empty;
        CheckFeedbackBorder.Visibility = Visibility.Collapsed;
        CheckAnswerPanel.Visibility = Visibility.Collapsed;
        CheckSelfGradePanel.Visibility = Visibility.Collapsed;
        SetCheckStage(CheckStage.Answering);
        RenderCheckProgress();
        CheckAnswerInput.Focus();
    }

    private void CheckSubmitButton_Click(object sender, RoutedEventArgs e) => _ = SubmitCheckAnswerAsync(CheckAnswerInput.Text);

    private void CheckNextButton_Click(object sender, RoutedEventArgs e) => NextCheckQuestion();

    private void CheckRevealButton_Click(object sender, RoutedEventArgs e) => RevealCheckAnswer();

    private void CheckSelfRightButton_Click(object sender, RoutedEventArgs e) => GradeCheckYourself(true);

    private void CheckSelfWrongButton_Click(object sender, RoutedEventArgs e) => GradeCheckYourself(false);

    private void ExitCheckButton_Click(object sender, RoutedEventArgs e) => ExitCheck();

    private void CheckAnswerInput_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        CheckAnswerPlaceholder.Visibility = string.IsNullOrEmpty(CheckAnswerInput.Text) ? Visibility.Visible : Visibility.Collapsed;
        if (CheckStageNow == CheckStage.Answering)
            CheckSubmitButton.IsEnabled = !string.IsNullOrWhiteSpace(CheckAnswerInput.Text);
    }

    private async void CheckAudioButton_Click(object sender, RoutedEventArgs e)
    {
        var card = CurrentCheckQuestion?.Card;
        if (card is null || string.IsNullOrWhiteSpace(card.Phrase))
            return;
        var voice = string.IsNullOrWhiteSpace(_settings.TtsVoice) ? EdgeNeuralTtsService.DefaultVoice : _settings.TtsVoice;
        var speed = _settings.TtsSpeed is >= 0.8 and <= 1.2 ? _settings.TtsSpeed : 1.0;
        await _audio.ToggleAsync(card.Phrase, voice, speed);
    }

    /// <summary>Checks the typed answer to the current question.</summary>
    public async Task SubmitCheckAnswerAsync(string? answer)
    {
        var question = CurrentCheckQuestion;
        if (question is null || CheckStageNow != CheckStage.Answering || string.IsNullOrWhiteSpace(answer))
            return;

        if (question.Direction == VocabularyCheckDirection.MeaningToWord)
        {
            var result = VocabularyCheckRules.CheckWord(question.Card.Phrase, answer);
            if (result == WordAnswerResult.Close && !_checkHadSecondTry)
            {
                _checkHadSecondTry = true;
                ShowCheckFeedback("Almost!", "Check the spelling and try again.", Warning);
                CheckAnswerInput.SelectAll();
                CheckAnswerInput.Focus();
                return;
            }

            RecordCheckAnswer(result == WordAnswerResult.Correct, result == WordAnswerResult.Close ? "Still not the right spelling." : null);
            return;
        }

        var card = question.Card;
        if (VocabularyChecker is null)
        {
            AskToGradeYourself("Compare with the meaning below and mark your answer.");
            return;
        }

        var cts = _checkCts;
        SetCheckStage(CheckStage.Grading);
        ShowCheckFeedback("Checking…", string.Empty, Neutral);
        try
        {
            var expected = MeaningFor(card);
            if (!string.Equals(expected, card.Meaning, StringComparison.Ordinal))
                expected = $"{expected} (English: {card.Meaning})";
            var grade = await VocabularyChecker.GradeMeaningAsync(card.Phrase, expected, answer, MeaningLanguage, cts?.Token ?? default);
            if (!ReferenceEquals(_checkCts, cts) || CurrentCheckQuestion != question || cts?.IsCancellationRequested == true)
                return;
            RecordCheckAnswer(grade.IsCorrect, grade.Feedback);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception error)
        {
            if (ReferenceEquals(_checkCts, cts) && CurrentCheckQuestion == question)
                AskToGradeYourself($"The AI could not check it ({error.Message}). Compare with the meaning below and mark your answer.");
        }
    }

    /// <summary>Shows the answer; the word counts as missed.</summary>
    public void RevealCheckAnswer()
    {
        if (CheckStageNow != CheckStage.Answering || CurrentCheckQuestion is null)
            return;
        RecordCheckAnswer(false, "You looked at the answer — this word comes back in your next study session.");
    }

    public void GradeCheckYourself(bool correct)
    {
        if (CheckStageNow != CheckStage.SelfGrade)
            return;
        RecordCheckAnswer(correct, null);
    }

    private void AskToGradeYourself(string message)
    {
        ShowCheckFeedback("Your call", message, Neutral);
        ShowCheckAnswer();
        CheckSelfGradePanel.Visibility = Visibility.Visible;
        SetCheckStage(CheckStage.SelfGrade);
        CheckSelfRightButton.Focus();
    }

    private void RecordCheckAnswer(bool correct, string? note)
    {
        var question = CurrentCheckQuestion;
        if (question is null)
            return;

        var rating = VocabularyCheckRules.RatingFor(correct, _checkHadSecondTry);
        ChangeSettings(FlashcardRules.ApplyRating(_settings, question.Card.CardKey, rating, DateTimeOffset.UtcNow));
        if (correct)
            _checkCorrect++;
        else
            _checkMissed.Add(question);

        ShowCheckFeedback(correct ? (_checkHadSecondTry ? "Correct (after a typo)" : "Correct!") : "Not quite", note ?? string.Empty, correct ? Success : Danger);
        ShowCheckAnswer();
        CheckSelfGradePanel.Visibility = Visibility.Collapsed;
        SetCheckStage(CheckStage.Answered);
        RenderCheckProgress();
        CheckNextButton.Focus();
    }

    public void NextCheckQuestion()
    {
        if (CheckStageNow != CheckStage.Answered)
            return;
        _checkIndex++;
        ShowCheckQuestion();
    }

    private void ShowCheckAnswer()
    {
        var card = CurrentCheckQuestion?.Card;
        if (card is null)
            return;
        var meaning = MeaningFor(card);
        CheckAnswerPhraseText.Text = card.Phrase;
        CheckAnswerIpaText.Text = card.PronunciationIpa ?? string.Empty;
        CheckAnswerMeaningText.Text = meaning;
        CheckAnswerEnglishText.Text = string.Equals(meaning, card.Meaning, StringComparison.Ordinal) ? string.Empty : card.Meaning;
        CheckAnswerEnglishText.Visibility = CheckAnswerEnglishText.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        CheckAnswerExampleText.Text = card.Example;
        CheckAnswerExampleText.Visibility = string.IsNullOrWhiteSpace(card.Example) ? Visibility.Collapsed : Visibility.Visible;
        CheckAnswerPanel.Visibility = Visibility.Visible;
    }

    private void ShowCheckSummary()
    {
        _audio.Stop();
        var total = _checkQuestions.Count;
        CheckSummaryTitle.Text = $"{_checkCorrect} / {total} correct";
        CheckSummaryText.Text = _checkMissed.Count == 0
            ? "You knew every word. They are marked remembered."
            : "These words go back to Learning so you study them again:";
        CheckMissedList.ItemsSource = _checkMissed.Select(question => new MissedWord(question.Card.Phrase, MeaningFor(question.Card))).ToList();
        CheckRetryMissedButton.Visibility = _checkMissed.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        SetCheckStage(CheckStage.Summary);
        RenderCheckProgress();
        (_checkMissed.Count > 0 ? CheckRetryMissedButton : CheckAgainButton).Focus();
    }

    public void ExitCheck()
    {
        CancelCheck();
        _audio.Stop();
        _checkQuestions = [];
        _checkMissed.Clear();
        SetCheckStage(CheckStage.Off);
        InPageCheckView.Visibility = Visibility.Collapsed;
        DeckManagementView.Visibility = Visibility.Visible;
        RefreshDecksList();
        UpdateDeckContentView();
        Focus();
    }

    private void CancelCheck()
    {
        var cts = _checkCts;
        _checkCts = null;
        if (cts is null)
            return;
        try
        {
            cts.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }

        cts.Dispose();
    }

    private void SetCheckStage(CheckStage stage)
    {
        CheckStageNow = stage;
        var question = stage is CheckStage.Answering or CheckStage.Grading or CheckStage.SelfGrade or CheckStage.Answered;
        CheckQuestionPanel.Visibility = stage == CheckStage.Summary ? Visibility.Collapsed : Visibility.Visible;
        CheckSummaryPanel.Visibility = stage == CheckStage.Summary ? Visibility.Visible : Visibility.Collapsed;
        CheckAnswerInput.Visibility = question ? Visibility.Visible : Visibility.Collapsed;
        CheckAnswerPlaceholder.Visibility = question && string.IsNullOrEmpty(CheckAnswerInput.Text) ? Visibility.Visible : Visibility.Collapsed;
        CheckAnswerInput.IsReadOnly = stage != CheckStage.Answering;
        CheckRevealButton.Visibility = question ? Visibility.Visible : Visibility.Collapsed;
        CheckRevealButton.IsEnabled = stage == CheckStage.Answering;
        CheckSubmitButton.Visibility = stage is CheckStage.Answering or CheckStage.Grading ? Visibility.Visible : Visibility.Collapsed;
        CheckSubmitButton.IsEnabled = stage == CheckStage.Answering && !string.IsNullOrWhiteSpace(CheckAnswerInput.Text);
        CheckNextButton.Visibility = stage == CheckStage.Answered ? Visibility.Visible : Visibility.Collapsed;
        CheckNextButton.Content = _checkIndex >= _checkQuestions.Count - 1 ? "See result (Enter)" : "Next (Enter)";

        // Hearing the word would give the answer away until it is answered.
        var direction = CurrentCheckQuestion?.Direction;
        CheckAudioButton.Visibility = question && (direction == VocabularyCheckDirection.WordToMeaning || stage is CheckStage.Answered or CheckStage.SelfGrade)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void RenderCheckProgress()
    {
        var total = _checkQuestions.Count;
        CheckProgressText.Text = CheckStageNow == CheckStage.Summary || total == 0
            ? $"{total} words"
            : $"Word {Math.Min(_checkIndex + 1, total)} / {total}";
        CheckScoreText.Text = $"✓ {_checkCorrect}   ✗ {_checkMissed.Count}";
    }

    private enum FeedbackTone { Neutral, Success, Warning, Danger }

    private const FeedbackTone Neutral = FeedbackTone.Neutral;
    private const FeedbackTone Success = FeedbackTone.Success;
    private const FeedbackTone Warning = FeedbackTone.Warning;
    private const FeedbackTone Danger = FeedbackTone.Danger;

    private void ShowCheckFeedback(string title, string text, FeedbackTone tone)
    {
        var (accent, background) = tone switch
        {
            FeedbackTone.Success => (Color.FromRgb(0x34, 0xD3, 0x99), Color.FromArgb(0x26, 0x05, 0x96, 0x69)),
            FeedbackTone.Warning => (Color.FromRgb(0xFB, 0xBF, 0x24), Color.FromArgb(0x26, 0xD9, 0x77, 0x06)),
            FeedbackTone.Danger => (Color.FromRgb(0xFC, 0xA5, 0xA5), Color.FromArgb(0x26, 0xDC, 0x26, 0x26)),
            _ => (Color.FromRgb(0x93, 0xC5, 0xFD), Color.FromArgb(0x26, 0x25, 0x63, 0xEB))
        };
        CheckFeedbackBorder.BorderBrush = new SolidColorBrush(accent);
        CheckFeedbackBorder.Background = new SolidColorBrush(background);
        CheckFeedbackTitle.Foreground = new SolidColorBrush(accent);
        CheckFeedbackTitle.Text = title;
        CheckFeedbackText.Text = text;
        CheckFeedbackText.Visibility = string.IsNullOrWhiteSpace(text) ? Visibility.Collapsed : Visibility.Visible;
        CheckFeedbackBorder.Visibility = Visibility.Visible;
    }

    /// <summary>Keys while checking: Enter checks or goes on, 1/2 grade yourself, Esc leaves. Typing is left alone.</summary>
    private bool HandleCheckKey(Key key)
    {
        switch (key)
        {
            case Key.Escape:
                ExitCheck();
                return true;
            case Key.Enter when CheckStageNow == CheckStage.Answering:
                _ = SubmitCheckAnswerAsync(CheckAnswerInput.Text);
                return true;
            case Key.Enter when CheckStageNow == CheckStage.Answered:
                NextCheckQuestion();
                return true;
            case Key.D1 or Key.NumPad1 when CheckStageNow == CheckStage.SelfGrade:
                GradeCheckYourself(false);
                return true;
            case Key.D2 or Key.NumPad2 when CheckStageNow == CheckStage.SelfGrade:
                GradeCheckYourself(true);
                return true;
            default:
                return false;
        }
    }

    #endregion
}
