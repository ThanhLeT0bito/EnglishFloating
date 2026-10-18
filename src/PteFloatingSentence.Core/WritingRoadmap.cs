namespace PteFloatingSentence.Core;

/// <summary>A kind of exercise at a level, e.g. Translate · Easy.</summary>
public sealed record RoadmapPractice(WritingPracticeMode Mode, WritingLevel Level);

/// <summary>
/// One stage of the writing roadmap: the grammar topics to master (in learning order) and the exercises that train
/// them. <see cref="Practices"/>[0] is the main exercise of the stage.
/// </summary>
public sealed record RoadmapStage(
    int Number,
    string Key,
    string Title,
    string VietnameseTitle,
    string Cefr,
    string Goal,
    IReadOnlyList<string> TopicKeys,
    IReadOnlyList<RoadmapPractice> Practices)
{
    /// <summary>False for a stage whose exercises are not built yet.</summary>
    public bool IsAvailable => TopicKeys.Count > 0 && Practices.Count > 0;

    public IReadOnlyList<GrammarTopic> Topics => TopicKeys.Select(GrammarTopics.Find).OfType<GrammarTopic>().ToList();
}

/// <summary>The writing and grammar roadmap from simple sentences to PTE tasks.</summary>
public static class WritingRoadmap
{
    public static IReadOnlyList<RoadmapStage> All { get; } =
    [
        new(1, "simple-sentences", "Simple sentences", "Câu đơn chuẩn", "A1–A2",
            "Write short, correct sentences: the right verb form, articles and basic prepositions.",
            ["present-simple", "past-simple", "articles", "countable-uncountable", "prepositions", "subject-verb-agreement"],
            [new(WritingPracticeMode.Translate, WritingLevel.Easy), new(WritingPracticeMode.Dictation, WritingLevel.Easy)]),
        new(2, "tenses", "Getting tenses right", "Đúng thì", "A2–B1",
            "Choose the tense that fits the time and situation, and ask questions correctly.",
            ["present-continuous", "present-perfect", "past-continuous", "future", "questions", "pronouns", "quantifiers"],
            [new(WritingPracticeMode.Translate, WritingLevel.Medium), new(WritingPracticeMode.Dictation, WritingLevel.Medium), new(WritingPracticeMode.Reverse, WritingLevel.Easy)]),
        new(3, "linking-ideas", "Linking ideas", "Nối ý", "B1",
            "Join ideas into longer sentences with linking words, relative clauses and the right verb patterns.",
            ["linking-words", "relative-clauses", "gerund-infinitive", "comparisons", "modal-verbs", "word-order"],
            [new(WritingPracticeMode.Combine, WritingLevel.Medium), new(WritingPracticeMode.Translate, WritingLevel.Medium), new(WritingPracticeMode.Combine, WritingLevel.Easy)]),
        new(4, "academic-sentences", "Academic sentences", "Câu phức học thuật", "B1–B2",
            "Write the complex sentences PTE expects: passive, conditionals, reported speech and participle clauses.",
            ["passive-voice", "conditionals", "past-perfect", "present-perfect-continuous", "reported-speech", "participle-clauses"],
            [new(WritingPracticeMode.Combine, WritingLevel.Hard), new(WritingPracticeMode.Translate, WritingLevel.Hard), new(WritingPracticeMode.Paraphrase, WritingLevel.Medium)]),
        new(5, "flexible-expression", "Flexible expression", "Diễn đạt linh hoạt", "B2",
            "Say one idea in several ways with the right word forms, collocations and prepositions.",
            ["word-form", "adjective-adverb", "collocations", "dependent-prepositions", "phrasal-verbs", "inversion"],
            [new(WritingPracticeMode.Paraphrase, WritingLevel.Hard), new(WritingPracticeMode.Dictation, WritingLevel.Hard), new(WritingPracticeMode.Translate, WritingLevel.Hard)]),
        new(6, "paragraphs-pte", "Paragraphs & PTE tasks", "Đoạn văn & bài PTE", "B2–C1",
            "Write connected paragraphs, then PTE Summarize Written Text and Essay. Coming soon.",
            [],
            [])
    ];

    public static RoadmapStage? Find(string? key) => All.FirstOrDefault(stage => string.Equals(stage.Key, key, StringComparison.Ordinal));

    /// <summary>The stage a grammar topic belongs to.</summary>
    public static RoadmapStage? StageOf(string? topicKey) =>
        All.FirstOrDefault(stage => stage.TopicKeys.Contains(topicKey ?? string.Empty, StringComparer.Ordinal));
}
