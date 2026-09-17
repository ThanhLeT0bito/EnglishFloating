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

    public static VocabularyItem? FindEquivalent(IEnumerable<VocabularyItem>? items, string phrase)
    {
        if (items is null)
            return null;

        var normalized = NormalizePhrase(phrase).ToLowerInvariant();
        if (string.IsNullOrEmpty(normalized))
            return null;

        var direct = items.FirstOrDefault(item => string.Equals(item.NormalizedPhrase, normalized, StringComparison.OrdinalIgnoreCase));
        if (direct is not null)
            return direct;

        var unpunctuated = NormalizePhrase(TrimPunctuation(phrase)).ToLowerInvariant();
        if (!string.IsNullOrEmpty(unpunctuated) && unpunctuated != normalized)
        {
            return items.FirstOrDefault(item => string.Equals(item.NormalizedPhrase, unpunctuated, StringComparison.OrdinalIgnoreCase));
        }

        return null;
    }

    public static bool ContainsEquivalent(IEnumerable<VocabularyItem>? items, string phrase) =>
        FindEquivalent(items, phrase) is not null;

    public static VocabularyItem CreatePending(string phrase)
    {
        var validation = ValidatePhrase(phrase);
        if (!validation.IsValid)
            throw new ArgumentException(validation.Error, nameof(phrase));

        var normalized = NormalizePhrase(phrase);
        return new VocabularyItem(
            Id: Guid.NewGuid(),
            Phrase: normalized,
            NormalizedPhrase: normalized.ToLowerInvariant(),
            Status: VocabularyStatus.Pending);
    }
}
