namespace PteFloatingSentence.Core;

public enum VocabularyStatus
{
    Pending,
    Ready,
    Failed
}

public sealed record VocabularyItem(
    Guid Id,
    string Phrase,
    string NormalizedPhrase,
    string? Meaning = null,
    string? Example = null,
    string? PronunciationIpa = null,
    VocabularyStatus Status = VocabularyStatus.Pending,
    bool IsHidden = false,
    string? LastError = null);

public sealed record StudySentence(
    Guid Id,
    string Text,
    bool IsCompleted = false,
    IReadOnlyList<VocabularyItem>? Vocabulary = null)
{
    public IReadOnlyList<VocabularyItem> Vocabulary { get; init; } = Vocabulary ?? [];
}

public sealed record StudyList(
    Guid Id,
    string Name,
    int TargetSentenceCount,
    int CurrentSentenceIndex,
    IReadOnlyList<StudySentence> Sentences);

