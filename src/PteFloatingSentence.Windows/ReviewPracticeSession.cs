using PteFloatingSentence.Core;

namespace PteFloatingSentence.Windows;

public sealed class ReviewPracticeSession
{
    private readonly Action<Guid, bool>? _onSentenceCompleted;
    private readonly int? _fixedSeed;
    private readonly HashSet<Guid> _completedSentenceIds;

    public StudyList List { get; }
    public int SentenceIndex { get; private set; }
    public ReviewSentence CurrentReview { get; private set; } = default!;
    public int CurrentHiddenPosition { get; private set; }
    public bool IsComplete { get; private set; }
    public bool IsAllSentencesCompleted { get; private set; }

    public ReviewPracticeSession(StudyList list, int? seed = null, Action<Guid, bool>? onSentenceCompleted = null)
    {
        List = list ?? throw new ArgumentNullException(nameof(list));
        _fixedSeed = seed;
        _onSentenceCompleted = onSentenceCompleted;
        _completedSentenceIds = new HashSet<Guid>(list.Sentences.Where(s => s.IsCompleted).Select(s => s.Id));

        if (list.Sentences.Count == 0)
        {
            SentenceIndex = 0;
            CurrentReview = new ReviewSentence(Guid.Empty, string.Empty, [], []);
            CurrentHiddenPosition = 0;
            IsComplete = true;
            IsAllSentencesCompleted = true;
            return;
        }

        var incompleteIndex = -1;
        for (var i = 0; i < list.Sentences.Count; i++)
        {
            if (!list.Sentences[i].IsCompleted)
            {
                incompleteIndex = i;
                break;
            }
        }

        if (incompleteIndex >= 0)
        {
            SentenceIndex = incompleteIndex;
            IsAllSentencesCompleted = false;
        }
        else
        {
            SentenceIndex = 0;
            IsAllSentencesCompleted = true;
        }

        LoadCurrentSentence();
    }

    public ReviewAnswerResult Submit(string answer)
    {
        if (List.Sentences.Count == 0 || CurrentReview.HiddenTokenIndexes.Count == 0)
        {
            return new ReviewAnswerResult(true, true, 0, null);
        }

        var result = ReviewPracticeRules.CheckAnswer(CurrentReview, CurrentHiddenPosition, answer);
        if (result.IsCorrect)
        {
            if (result.IsComplete)
            {
                IsComplete = true;
                _completedSentenceIds.Add(CurrentReview.SentenceId);
                if (List.Sentences.Count > 0 && _completedSentenceIds.Count >= List.Sentences.Count)
                {
                    IsAllSentencesCompleted = true;
                }
                _onSentenceCompleted?.Invoke(CurrentReview.SentenceId, true);
            }
            else
            {
                CurrentHiddenPosition = result.NextHiddenTokenPosition;
            }
        }

        return result;
    }

    public void MoveNextSentence()
    {
        if (List.Sentences.Count <= 1) return;
        SentenceIndex = (SentenceIndex + 1) % List.Sentences.Count;
        LoadCurrentSentence();
    }

    public void MovePreviousSentence()
    {
        if (List.Sentences.Count <= 1) return;
        SentenceIndex = (SentenceIndex - 1 + List.Sentences.Count) % List.Sentences.Count;
        LoadCurrentSentence();
    }

    public void RestartList()
    {
        if (List.Sentences.Count == 0) return;
        _completedSentenceIds.Clear();
        SentenceIndex = 0;
        IsAllSentencesCompleted = false;
        LoadCurrentSentence();
    }

    private void LoadCurrentSentence()
    {
        if (List.Sentences.Count == 0)
        {
            CurrentReview = new ReviewSentence(Guid.Empty, string.Empty, [], []);
            CurrentHiddenPosition = 0;
            IsComplete = true;
            return;
        }

        var sentence = List.Sentences[SentenceIndex];
        CurrentReview = ReviewPracticeRules.CreateProjection(sentence, _fixedSeed);
        CurrentHiddenPosition = 0;
        IsComplete = IsAllSentencesCompleted || CurrentReview.HiddenTokenIndexes.Count == 0;
    }

}
