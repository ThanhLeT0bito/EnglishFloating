namespace PteFloatingSentence.Core;

public enum WritingLevel
{
    Easy = 0,
    Medium = 1,
    Hard = 2
}

public enum WritingExplanationLanguage
{
    English = 0,
    Vietnamese = 1
}

/// <summary>A translation topic. Built-in keys are stable slugs; custom keys start with <see cref="WritingTopics.CustomKeyPrefix"/>.</summary>
public sealed record WritingTopic(string Key, string Name);

public enum WritingIssueType
{
    Grammar = 0,
    Vocabulary = 1,
    Spelling = 2,
    WordOrder = 3,
    MissingMeaning = 4,
    ExtraMeaning = 5,
    Punctuation = 6
}

public enum WritingVerdict
{
    NeedsWork = 0,
    Almost = 1,
    Correct = 2
}

/// <summary>Tip tiers in reveal order, from mild to strong help.</summary>
public enum WritingTipTier
{
    Vocabulary = 0,
    Grammar = 1,
    Starter = 2
}

public sealed record WritingVocabularyHint(string Vietnamese, string English, string PartOfSpeech);

public sealed record WritingTips(IReadOnlyList<WritingVocabularyHint> Vocabulary, string Grammar, string Starter)
{
    public static WritingTips Empty { get; } = new([], string.Empty, string.Empty);
}

/// <summary>A generated exercise. <see cref="ReferenceEnglish"/> stays hidden from the learner until checking.</summary>
public sealed record WritingPrompt(string Vietnamese, string ReferenceEnglish, string Topic, WritingLevel Level, WritingTips Tips)
{
    /// <summary>How the prompt was generated, for the timing line on the Writing page.</summary>
    public AiCallInfo? Call { get; init; }

    /// <summary>Weak points this sentence was written to practice; empty for a normal sentence.</summary>
    public IReadOnlyList<WritingSkillTag> FocusTags { get; init; } = [];

    /// <summary>
    /// Kind of exercise. Translate prompts also serve dictation; paraphrase and combine prompts carry
    /// <see cref="SourceEnglish"/>; a reverse prompt's answer is <see cref="Vietnamese"/>.
    /// </summary>
    public WritingPracticeMode Mode { get; init; } = WritingPracticeMode.Translate;

    /// <summary>English text the learner works from: the sentence to paraphrase, or the two sentences to combine (one per line).</summary>
    public string SourceEnglish { get; init; } = string.Empty;

    /// <summary>Instruction shown with the prompt, e.g. the connector to use when combining.</summary>
    public string Instruction { get; init; } = string.Empty;
}

/// <summary>One problem in the learner's answer. <see cref="Original"/> is copied from the answer and is empty for missing meaning.</summary>
public sealed record WritingIssue(WritingIssueType Type, string Original, string Suggestion, string Explanation)
{
    private readonly WritingSkillTag? _tag;

    /// <summary>Fine-grained grammar point; defaults from <see cref="Type"/> when the AI gives none.</summary>
    public WritingSkillTag Tag
    {
        get => _tag ?? WritingSkillTags.DefaultFor(Type);
        init => _tag = value;
    }
}

public sealed record WritingFeedback(
    int Score,
    WritingVerdict Verdict,
    string CorrectedSentence,
    string? NaturalAlternative,
    string Summary,
    IReadOnlyList<WritingIssue> Issues)
{
    /// <summary>How the check was answered, for the timing line on the Writing page.</summary>
    public AiCallInfo? Call { get; init; }
}

/// <summary>A highlighted span of the learner's answer that belongs to <c>Issues[IssueIndex]</c>.</summary>
public readonly record struct WritingHighlight(int Start, int Length, int IssueIndex);
