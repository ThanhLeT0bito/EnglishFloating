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

    public static bool ContainsEquivalent(IEnumerable<VocabularyItem>? items, string phrase)
    {
        if (items is null)
            return false;

        var normalized = NormalizePhrase(phrase).ToLowerInvariant();
        if (string.IsNullOrEmpty(normalized))
            return false;

        return items.Any(item => string.Equals(item.NormalizedPhrase, normalized, StringComparison.OrdinalIgnoreCase));
    }

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
