using PteFloatingSentence.Core;

namespace PteFloatingSentence.Windows;

public sealed class ReviewPracticeSession
{
    private readonly Action<Guid, bool>? _onSentenceCompleted;
    private readonly int? _fixedSeed;
    private readonly HashSet<Guid> _completedSentenceIds;

    public StudyList List { get; private set; }
    public int SentenceIndex { get; private set; }
    public ReviewSentence CurrentReview { get; private set; } = default!;
    public int CurrentHiddenPosition { get; private set; }
    public bool IsComplete { get; private set; }
    public bool IsAllSentencesCompleted { get; private set; }
    public PracticeMode Mode { get; private set; }

    public ReviewPracticeSession(StudyList list, int? seed = null, Action<Guid, bool>? onSentenceCompleted = null, PracticeMode mode = PracticeMode.TextHints)
    {
        List = list ?? throw new ArgumentNullException(nameof(list));
        _fixedSeed = seed;
        _onSentenceCompleted = onSentenceCompleted;
        Mode = mode;
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

    public bool RefreshList(StudyList list)
    {
        ArgumentNullException.ThrowIfNull(list);
        var changed = List.Id != list.Id || !List.Sentences.Select(s => (s.Id, s.Text))
            .SequenceEqual(list.Sentences.Select(s => (s.Id, s.Text)));
        var selectedId = CurrentReview.SentenceId;
        if (List.Id != list.Id) _completedSentenceIds.Clear();
        var previousIds = List.Id == list.Id ? List.Sentences.Select(s => s.Id).ToHashSet() : [];
        _completedSentenceIds.IntersectWith(list.Sentences.Select(s => s.Id));
        _completedSentenceIds.UnionWith(list.Sentences.Where(s => s.IsCompleted && !previousIds.Contains(s.Id)).Select(s => s.Id));
        List = list;
        if (!changed) return false;
        var selectedIndex = list.Sentences.ToList().FindIndex(s => s.Id == selectedId);
        SentenceIndex = selectedIndex >= 0 ? selectedIndex : Math.Clamp(SentenceIndex, 0, Math.Max(0, list.Sentences.Count - 1));
        IsAllSentencesCompleted = _completedSentenceIds.Count == list.Sentences.Count;
        LoadCurrentSentence();
        return true;
    }

    public ReviewAnswerResult Submit(string answer)
    {
        if (IsComplete || CurrentReview.HiddenTokenIndexes.Count == 0)
        {
            return new ReviewAnswerResult(false, IsComplete, CurrentHiddenPosition, "Try again.");
        }

        var result = ReviewPracticeRules.CheckAnswer(CurrentReview, CurrentHiddenPosition, answer);
        if (result.IsCorrect)
        {
            if (result.IsComplete)
            {
                CompleteCurrentSentence();
            }
            else
            {
                CurrentHiddenPosition = result.NextHiddenTokenPosition;
            }
        }

        return result;
    }

    public ReviewAnswerResult SubmitDictation(string answer)
    {
        if (IsComplete)
            return new ReviewAnswerResult(false, IsComplete, CurrentHiddenPosition, "Try again.");

        if (!ReviewPracticeRules.IsCorrectDictation(CurrentReview.OriginalText, answer))
            return new ReviewAnswerResult(false, false, CurrentHiddenPosition, "Try again.");

        CompleteCurrentSentence();
        return new ReviewAnswerResult(true, true, CurrentHiddenPosition, null);
    }

    private void CompleteCurrentSentence()
    {
        IsComplete = true;
        if (!_completedSentenceIds.Add(CurrentReview.SentenceId))
            return;
        if (_completedSentenceIds.Count >= List.Sentences.Count)
            IsAllSentencesCompleted = true;
        _onSentenceCompleted?.Invoke(CurrentReview.SentenceId, true);
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

    public void SetMode(PracticeMode mode)
    {
        if (Mode == mode) return;
        Mode = mode;
        LoadCurrentSentence();
    }

    public ReviewAnswerResult SkipCurrentWord()
    {
        if (IsComplete || CurrentReview.HiddenTokenIndexes.Count == 0)
        {
            return new ReviewAnswerResult(false, IsComplete, CurrentHiddenPosition, null);
        }

        var isLast = CurrentHiddenPosition >= CurrentReview.HiddenTokenIndexes.Count - 1;
        if (isLast)
        {
            CompleteCurrentSentence();
            return new ReviewAnswerResult(true, true, CurrentHiddenPosition, null);
        }
        else
        {
            CurrentHiddenPosition++;
            return new ReviewAnswerResult(true, false, CurrentHiddenPosition, null);
        }
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
        CurrentReview = ReviewPracticeRules.CreateProjection(sentence, _fixedSeed, Mode);
        CurrentHiddenPosition = 0;
        IsComplete = _completedSentenceIds.Contains(sentence.Id);
    }

}
