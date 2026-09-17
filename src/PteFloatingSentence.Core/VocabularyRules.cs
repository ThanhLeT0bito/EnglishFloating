using System.Text.RegularExpressions;

namespace PteFloatingSentence.Core;

public static partial class VocabularyRules
{
    private static readonly Regex WhitespaceRegex = new(@"\s+", RegexOptions.Compiled);

    public static string NormalizePhrase(string? phrase)
    {
        if (string.IsNullOrWhiteSpace(phrase))
            return string.Empty;

        return WhitespaceRegex.Replace(phrase.Trim(), " ");
    }

    public static ValidationResult ValidatePhrase(string? phrase)
    {
        var normalized = NormalizePhrase(phrase);
        if (string.IsNullOrEmpty(normalized))
            return new(false, "Enter a word or phrase.");

        var words = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return words.Length <= 20
            ? new(true, null)
            : new(false, "Use 20 words or fewer.");
    }

    private static readonly char[] PunctuationChars = ['.', ',', ';', '!', '?', ':', '"', '\'', '(', ')', '[', ']', '“', '”', '‘', '’'];

    public static string TrimPunctuation(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        return text.Trim().Trim(PunctuationChars);
    }

    public static string CleanPhrase(string? phrase)
    {
        if (string.IsNullOrWhiteSpace(phrase))
            return string.Empty;

        return NormalizePhrase(TrimPunctuation(phrase)).ToLowerInvariant();
    }

    public static VocabularyItem? FindEquivalent(IEnumerable<VocabularyItem>? items, string phrase)
    {
        if (items is null)
            return null;

        var queryClean = CleanPhrase(phrase);
        if (string.IsNullOrEmpty(queryClean))
            return null;

        return items.FirstOrDefault(item =>
            CleanPhrase(item.NormalizedPhrase) == queryClean ||
            CleanPhrase(item.Phrase) == queryClean);
    }

    public static bool ContainsEquivalent(IEnumerable<VocabularyItem>? items, string phrase) =>
        FindEquivalent(items, phrase) is not null;

    public static VocabularyItem CreatePending(string phrase)
    {
        var cleaned = TrimPunctuation(phrase);
        var validation = ValidatePhrase(cleaned);
        if (!validation.IsValid)
            throw new ArgumentException(validation.Error, nameof(phrase));

        var normalized = NormalizePhrase(cleaned);
        return new VocabularyItem(
            Id: Guid.NewGuid(),
            Phrase: normalized,
            NormalizedPhrase: normalized.ToLowerInvariant(),
            Status: VocabularyStatus.Pending);
    }
}
