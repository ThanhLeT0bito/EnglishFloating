using System.ComponentModel;
using System.Runtime.CompilerServices;
using PteFloatingSentence.Core;

namespace PteFloatingSentence.Windows;

public sealed class ReviewPracticeViewModel : INotifyPropertyChanged
{
    private static readonly char[] TrimPunctuationChars = [
        '.', ',', '!', '?', ';', ':', '"', '\'', '(', ')', '[', ']', '{', '}', '-', '—'
    ];

    private string _currentInput = string.Empty;
    private string? _errorText;
    private string? _revealedAnswer;

    public ReviewPracticeSession Session { get; }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ReviewPracticeViewModel(ReviewPracticeSession session)
    {
        Session = session ?? throw new ArgumentNullException(nameof(session));
    }

    public IReadOnlyList<ReviewToken> DisplayTokens => Session.CurrentReview.Tokens;

    public string ProgressText => Session.List.Sentences.Count == 0
        ? "No sentences"
        : $"Sentence {Session.SentenceIndex + 1} of {Session.List.Sentences.Count}";

    public string CurrentInput
    {
        get => _currentInput;
        set
        {
            if (_currentInput != value)
            {
                _currentInput = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CanSubmit));
            }
        }
    }

    public string? ErrorText
    {
        get => _errorText;
        set
        {
            if (_errorText != value)
            {
                _errorText = value;
                OnPropertyChanged();
            }
        }
    }

    public string? RevealedAnswer
    {
        get => _revealedAnswer;
        set
        {
            if (_revealedAnswer != value)
            {
                _revealedAnswer = value;
                OnPropertyChanged();
            }
        }
    }

    public bool CanSubmit => !Session.IsComplete &&
                             Session.CurrentReview.HiddenTokenIndexes.Count > 0 &&
                             !string.IsNullOrWhiteSpace(CurrentInput);

    public bool IsComplete => Session.IsComplete;

    public bool IsAllCompleted => Session.IsAllSentencesCompleted;

    public string CurrentExpectedWord
    {
        get
        {
            if (Session.CurrentReview.HiddenTokenIndexes.Count == 0 ||
                Session.CurrentHiddenPosition >= Session.CurrentReview.HiddenTokenIndexes.Count)
            {
                return string.Empty;
            }

            var tokenIdx = Session.CurrentReview.HiddenTokenIndexes[Session.CurrentHiddenPosition];
            return Session.CurrentReview.Tokens[tokenIdx].SourceText.Trim().Trim(TrimPunctuationChars);
        }
    }

    public void Submit()
    {
        if (!CanSubmit) return;

        var result = Session.Submit(CurrentInput);
        if (result.IsCorrect)
        {
            CurrentInput = string.Empty;
            ErrorText = null;
            RevealedAnswer = null;
            NotifyAllProperties();
        }
        else
        {
            ErrorText = result.Error ?? "Try again.";
            OnPropertyChanged(nameof(ErrorText));
        }
    }

    public void RevealCurrentAnswer()
    {
        RevealedAnswer = CurrentExpectedWord;
    }

    public void NextSentence()
    {
        Session.MoveNextSentence();
        ResetSentenceInputs();
    }

    public void PreviousSentence()
    {
        Session.MovePreviousSentence();
        ResetSentenceInputs();
    }

    public void RestartList()
    {
        Session.RestartList();
        ResetSentenceInputs();
    }

    private void ResetSentenceInputs()
    {
        CurrentInput = string.Empty;
        ErrorText = null;
        RevealedAnswer = null;
        NotifyAllProperties();
    }

    private void NotifyAllProperties()
    {
        OnPropertyChanged(nameof(DisplayTokens));
        OnPropertyChanged(nameof(ProgressText));
        OnPropertyChanged(nameof(CurrentInput));
        OnPropertyChanged(nameof(ErrorText));
        OnPropertyChanged(nameof(RevealedAnswer));
        OnPropertyChanged(nameof(CanSubmit));
        OnPropertyChanged(nameof(IsComplete));
        OnPropertyChanged(nameof(IsAllCompleted));
        OnPropertyChanged(nameof(CurrentExpectedWord));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
