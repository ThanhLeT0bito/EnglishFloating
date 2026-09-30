namespace PteFloatingSentence.Core;

public sealed record SentencePhrasingResult(string Text, IReadOnlyList<int> BreakAfterWordIndices, ValidationResult Validation);

public static class SentencePhrasing
{
    public static SentencePhrasingResult ParseGroups(string? input)
    {
        var groups = (input ?? string.Empty).Trim().Replace("\r\n", "\n").Replace('\r', '\n')
            .Split('\n').Select(VocabularyRules.NormalizePhrase).ToArray();
        var text = string.Join(" ", groups);
        var validation = groups.Any(string.IsNullOrWhiteSpace)
            ? new ValidationResult(false, "Enter one nonempty reading group per line.")
            : SentenceValidator.Validate(text);
        if (!validation.IsValid)
            return new(text, [], validation);

        var breaks = new List<int>();
        var wordCount = 0;
        for (var i = 0; i < groups.Length - 1; i++)
        {
            wordCount += groups[i].Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
            breaks.Add(wordCount);
        }
        return new(text, breaks, validation);
    }

    public static IReadOnlyList<int> NormalizeBreaks(string text, IReadOnlyList<int>? boundaries)
    {
        var wordCount = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
        return (boundaries ?? []).Where(index => index > 0 && index < wordCount).Distinct().Order().ToArray();
    }
}
