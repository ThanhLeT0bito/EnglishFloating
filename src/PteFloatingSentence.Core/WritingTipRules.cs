namespace PteFloatingSentence.Core;

public static class WritingTipRules
{
    public const int MaxVocabularyItems = 5;
    public const int MaxStarterWords = 3;

    /// <summary>
    /// Cleans AI tips so they help without giving the answer away: drops empty or duplicate vocabulary,
    /// keeps at most <see cref="MaxVocabularyItems"/>, drops any item that equals the whole reference,
    /// cuts the starter to <see cref="MaxStarterWords"/> words and drops it when it would reveal the reference.
    /// </summary>
    public static WritingTips Sanitize(WritingTips? tips, string referenceEnglish)
    {
        if (tips is null)
            return WritingTips.Empty;

        var reference = Words(referenceEnglish);
        var referenceKey = Key(reference);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var vocabulary = new List<WritingVocabularyHint>();
        foreach (var item in tips.Vocabulary ?? [])
        {
            if (item is null)
                continue;
            var vietnamese = Collapse(item.Vietnamese);
            var english = Collapse(item.English);
            if (vietnamese.Length == 0 || english.Length == 0)
                continue;
            if (referenceKey.Length > 0 && Key(Words(english)) == referenceKey)
                continue;
            if (!seen.Add(vietnamese))
                continue;
            vocabulary.Add(new WritingVocabularyHint(vietnamese, english, Collapse(item.PartOfSpeech)));
            if (vocabulary.Count == MaxVocabularyItems)
                break;
        }

        var starterWords = Words(tips.Starter).Take(MaxStarterWords).ToArray();
        if (reference.Length > 0 && starterWords.Length >= reference.Length)
            starterWords = [];

        return new WritingTips(vocabulary, Collapse(tips.Grammar), string.Join(' ', starterWords));
    }

    /// <summary>Tiers that have content, in reveal order.</summary>
    public static IReadOnlyList<WritingTipTier> AvailableTiers(WritingTips? tips)
    {
        var tiers = new List<WritingTipTier>();
        if (tips is null)
            return tiers;
        if (tips.Vocabulary is { Count: > 0 })
            tiers.Add(WritingTipTier.Vocabulary);
        if (!string.IsNullOrWhiteSpace(tips.Grammar))
            tiers.Add(WritingTipTier.Grammar);
        if (!string.IsNullOrWhiteSpace(tips.Starter))
            tiers.Add(WritingTipTier.Starter);
        return tiers;
    }

    public static int WordCount(string? text) => Words(text).Length;

    private static string[] Words(string? text) =>
        (text ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

    private static string Collapse(string? text) => string.Join(' ', Words(text));

    private static string Key(string[] words) =>
        string.Join(' ', words.Select(word => new string(word.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant())
            .Where(word => word.Length > 0));
}
