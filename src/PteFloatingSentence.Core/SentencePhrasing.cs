using System.Text;
using System.Text.RegularExpressions;

namespace PteFloatingSentence.Core;

public sealed record SentencePhrasingResult(string Text, IReadOnlyList<int> BreakAfterWordIndices, ValidationResult Validation);

public static class SentencePhrasing
{
    public const string WideGap = "\u2003\u2003";

    public static string GetDisplayText(string text, IReadOnlyList<int>? boundaries) =>
        GetDisplayRange(text, 0, text.Length, boundaries);

    // Match against canonical text first; then apply spacing to each highlighted/plain run.
    public static string GetDisplayRange(string text, int start, int length, IReadOnlyList<int>? boundaries)
    {
        if (boundaries is null || boundaries.Count == 0) return text.Substring(start, length);
        var words = Regex.Matches(text, @"\S+");
        var gaps = NormalizeBreaks(text, boundaries)
            .ToDictionary(index => words[index - 1].Index + words[index - 1].Length, index => words[index].Index);
        var result = new StringBuilder();
        for (var index = start; index < start + length; index++)
        {
            if (gaps.TryGetValue(index, out var nextWord))
            {
                result.Append(WideGap);
                index = nextWord - 1;
            }
            else result.Append(text[index]);
        }
        return result.ToString();
    }

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
