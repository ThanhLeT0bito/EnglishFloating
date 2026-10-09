namespace PteFloatingSentence.Core;

/// <summary>How the learner practices: translate a Vietnamese sentence, or type an English sentence they hear.</summary>
public enum WritingPracticeMode
{
    /// <summary>Translate a Vietnamese sentence into English.</summary>
    Translate = 0,

    /// <summary>Type the English sentence you hear (PTE Write from Dictation).</summary>
    Dictation = 1,

    /// <summary>Rewrite an English sentence in your own words.</summary>
    Paraphrase = 2,

    /// <summary>Combine two short English sentences into one with a given connector.</summary>
    Combine = 3,

    /// <summary>Translate an English sentence into Vietnamese (checks understanding).</summary>
    Reverse = 4
}

/// <summary>Fine-grained grammar point of a mistake, used for statistics and weak-point practice.</summary>
public enum WritingSkillTag
{
    Other = 0,
    Tense = 1,
    Article = 2,
    Preposition = 3,
    SubjectVerbAgreement = 4,
    Plural = 5,
    WordForm = 6,
    WordChoice = 7,
    WordOrder = 8,
    Spelling = 9,
    MissingWord = 10,
    ExtraWord = 11,
    SentenceStructure = 12
}

/// <summary>One recorded mistake. <see cref="Source"/> is the Vietnamese prompt, or the English sentence in dictation.</summary>
public sealed record WritingMistake(
    DateTimeOffset At,
    WritingPracticeMode Mode,
    WritingIssueType Type,
    WritingSkillTag Tag,
    string Original,
    string Suggestion,
    string Explanation,
    string Source,
    string Answer,
    string Corrected);

/// <summary>Result of a sentence that targeted <see cref="Tag"/> in weak-point practice.</summary>
public sealed record WritingTagReview(DateTimeOffset At, WritingSkillTag Tag, bool Passed);

/// <summary>Saved practice history: the mistake notebook and weak-point review results.</summary>
public sealed record WritingHistory
{
    public int Version { get; init; } = 1;
    public IReadOnlyList<WritingMistake> Mistakes { get; init; } = [];
    public IReadOnlyList<WritingTagReview> Reviews { get; init; } = [];

    public static WritingHistory Empty { get; } = new();
}

/// <param name="Mistakes">Mistakes with this tag inside the statistics window.</param>
/// <param name="Passed">Weak-point sentences for this tag answered without that mistake inside the window.</param>
/// <param name="Weight">How weak the learner still is: mistakes minus passes, never below zero.</param>
public sealed record WritingTagStat(WritingSkillTag Tag, int Mistakes, int Passed, int Weight, DateTimeOffset LastSeen);

/// <summary>The same mistake (tag, original and suggestion) made <see cref="Count"/> times; <see cref="Latest"/> is the newest.</summary>
public sealed record WritingMistakeGroup(WritingMistake Latest, int Count);
