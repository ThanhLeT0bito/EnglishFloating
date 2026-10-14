namespace PteFloatingSentence.Core;

/// <summary>One line of a form table, e.g. "Positive" · "S + V(s/es)" · "She works from home."</summary>
public sealed record GrammarFormLine(string Label, string Pattern, string Example);

/// <summary>One use of a grammar point with an example.</summary>
public sealed record GrammarUse(string Title, string Explanation, string Example);

/// <summary>A typical mistake: the wrong sentence, the right one and why.</summary>
public sealed record GrammarMistakeExample(string Wrong, string Right, string Why);

public sealed record GrammarExample(string English, string Vietnamese);

/// <summary>A multiple-choice concept check. <see cref="AnswerIndex"/> points into <see cref="Options"/>.</summary>
public sealed record GrammarQuizItem(string Question, IReadOnlyList<string> Options, int AnswerIndex, string Explanation);

/// <summary>
/// A lesson about one grammar topic, written by the AI in the learner's explanation language and kept on disk.
/// It starts from the essence (what the form means and why it is used) before form, uses and examples.
/// </summary>
public sealed record GrammarLesson(
    string TopicKey,
    string Title,
    WritingExplanationLanguage Language,
    string Essence,
    IReadOnlyList<GrammarFormLine> Form,
    IReadOnlyList<GrammarUse> Uses,
    IReadOnlyList<string> SignalWords,
    string Contrast,
    IReadOnlyList<GrammarMistakeExample> CommonMistakes,
    IReadOnlyList<GrammarExample> Examples,
    IReadOnlyList<GrammarQuizItem> Quiz,
    DateTimeOffset CreatedAt)
{
    /// <summary>How the lesson was generated; not saved.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public AiCallInfo? Call { get; init; }
}

public static class GrammarLessonRules
{
    public const int MaxFormLines = 6;
    public const int MaxUses = 5;
    public const int MaxSignalWords = 10;
    public const int MaxMistakes = 5;
    public const int MaxExamples = 6;
    public const int MaxQuizItems = 4;
    public const int MaxTextLength = 600;

    /// <summary>
    /// Trims text, drops empty or broken entries (a quiz item needs 2-4 options and a valid answer) and caps each list.
    /// Returns null when the essence or every example is missing, so the caller can treat the lesson as invalid.
    /// </summary>
    public static GrammarLesson? Sanitize(GrammarLesson? lesson)
    {
        if (lesson is null)
            return null;

        var essence = Clean(lesson.Essence);
        var examples = (lesson.Examples ?? [])
            .Where(example => example is not null && Clean(example.English).Length > 0)
            .Select(example => new GrammarExample(Clean(example.English), Clean(example.Vietnamese)))
            .Take(MaxExamples)
            .ToList();
        if (essence.Length == 0 || examples.Count == 0 || string.IsNullOrWhiteSpace(lesson.TopicKey))
            return null;

        var quiz = new List<GrammarQuizItem>();
        foreach (var item in lesson.Quiz ?? [])
        {
            if (item is null || Clean(item.Question).Length == 0)
                continue;
            var options = (item.Options ?? []).Select(Clean).ToList();
            if (options.Count is < 2 or > 4 || options.Any(option => option.Length == 0) || item.AnswerIndex < 0 || item.AnswerIndex >= options.Count)
                continue;
            if (options.Distinct(StringComparer.OrdinalIgnoreCase).Count() != options.Count)
                continue;
            quiz.Add(new GrammarQuizItem(Clean(item.Question), options, item.AnswerIndex, Clean(item.Explanation)));
            if (quiz.Count == MaxQuizItems)
                break;
        }

        return lesson with
        {
            TopicKey = lesson.TopicKey.Trim(),
            Title = Clean(lesson.Title),
            Essence = essence,
            Form = (lesson.Form ?? [])
                .Where(line => line is not null && Clean(line.Pattern).Length > 0)
                .Select(line => new GrammarFormLine(Clean(line.Label), Clean(line.Pattern), Clean(line.Example)))
                .Take(MaxFormLines)
                .ToList(),
            Uses = (lesson.Uses ?? [])
                .Where(use => use is not null && (Clean(use.Title).Length > 0 || Clean(use.Explanation).Length > 0))
                .Select(use => new GrammarUse(Clean(use.Title), Clean(use.Explanation), Clean(use.Example)))
                .Take(MaxUses)
                .ToList(),
            SignalWords = (lesson.SignalWords ?? [])
                .Select(Clean)
                .Where(word => word.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(MaxSignalWords)
                .ToList(),
            Contrast = Clean(lesson.Contrast),
            CommonMistakes = (lesson.CommonMistakes ?? [])
                .Where(mistake => mistake is not null && Clean(mistake.Wrong).Length > 0 && Clean(mistake.Right).Length > 0)
                .Select(mistake => new GrammarMistakeExample(Clean(mistake.Wrong), Clean(mistake.Right), Clean(mistake.Why)))
                .Take(MaxMistakes)
                .ToList(),
            Examples = examples,
            Quiz = quiz
        };
    }

    /// <summary>Key of a cached lesson.</summary>
    public static string CacheKey(string topicKey, WritingExplanationLanguage language) => $"{topicKey}|{language}";

    private static string Clean(string? text)
    {
        var value = string.Join(' ', (text ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return value.Length <= MaxTextLength ? value : value[..MaxTextLength];
    }
}
