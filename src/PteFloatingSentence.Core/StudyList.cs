namespace PteFloatingSentence.Core;

public sealed record StudySentence(Guid Id, string Text, bool IsCompleted = false);

public sealed record StudyList(
    Guid Id,
    string Name,
    int TargetSentenceCount,
    int CurrentSentenceIndex,
    IReadOnlyList<StudySentence> Sentences);
