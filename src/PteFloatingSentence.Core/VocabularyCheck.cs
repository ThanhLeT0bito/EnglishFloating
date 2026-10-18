using System.Text;
using System.Text.RegularExpressions;

namespace PteFloatingSentence.Core;

/// <summary>A language flashcard meanings can be shown in. English is the meaning saved with each word.</summary>
public sealed record MeaningLanguage(string Code, string Name, string NativeName)
{
    public string DisplayName => string.Equals(Name, NativeName, StringComparison.Ordinal) ? Name : $"{Name} ({NativeName})";
}

/// <summary>The languages meanings can be shown in. Add a line here to offer another one.</summary>
public static class MeaningLanguages
{
    public const string English = "en";
    public const string Vietnamese = "vi";

    public static IReadOnlyList<MeaningLanguage> All { get; } =
    [
        new(English, "English", "English"),
        new(Vietnamese, "Vietnamese", "Tiếng Việt")
    ];

    public static MeaningLanguage Find(string? code) =>
        All.FirstOrDefault(language => string.Equals(language.Code, code?.Trim(), StringComparison.OrdinalIgnoreCase)) ?? All[0];

    public static string Normalize(string? code) => Find(code).Code;
}

/// <summary>What a vocabulary check asks.</summary>
public enum VocabularyCheckDirection
{
    /// <summary>Show the meaning; type the word.</summary>
    MeaningToWord = 0,

    /// <summary>Show the word; type its meaning.</summary>
    WordToMeaning = 1,

    /// <summary>Alternate between both.</summary>
    Mixed = 2
}

/// <summary>A word's English meaning in another language, made once by the AI and kept.</summary>
public sealed record MeaningTranslation(string Language, string NormalizedPhrase, string SourceMeaning, string Text);

public enum WordAnswerResult
{
    Wrong = 0,

    /// <summary>One or two letters off: the learner gets another try.</summary>
    Close = 1,
    Correct = 2
}

/// <summary>One question of a check: the card and which way it is asked.</summary>
public sealed record VocabularyCheckQuestion(FlashcardItem Card, VocabularyCheckDirection Direction);

/// <summary>Rules for checking vocabulary after studying flashcards.</summary>
public static class VocabularyCheckRules
{
    public const int DefaultQuestionCount = 10;
    public const int MaxTranslations = 5000;
    public const int MaxTranslationLength = 400;
    public const string Blank = "_____";

    private static readonly Regex Spaces = new(@"\s+", RegexOptions.Compiled);

    /// <summary>Settings with a known meaning language, a defined direction and clean, de-duplicated translations.</summary>
    public static AppSettings Normalize(AppSettings settings)
    {
        var seen = new HashSet<(string, string, string)>();
        var translations = new List<MeaningTranslation>();
        // Newest last: keep the last translation of each word and drop the oldest when over the cap.
        foreach (var item in (settings.MeaningTranslations ?? []).Reverse())
        {
            if (item is null)
                continue;
            var language = MeaningLanguages.Find(item.Language);
            if (!string.Equals(language.Code, item.Language?.Trim(), StringComparison.OrdinalIgnoreCase) || language.Code == MeaningLanguages.English)
                continue;
            var phrase = VocabularyRules.NormalizePhrase(item.NormalizedPhrase).ToLowerInvariant();
            var source = CleanText(item.SourceMeaning);
            var text = CleanText(item.Text);
            if (phrase.Length == 0 || text.Length == 0 || text.Length > MaxTranslationLength)
                continue;
            if (!seen.Add((language.Code, phrase, source)))
                continue;
            translations.Add(new MeaningTranslation(language.Code, phrase, source, text));
            if (translations.Count == MaxTranslations)
                break;
        }

        translations.Reverse();
        return settings with
        {
            FlashcardMeaningLanguage = MeaningLanguages.Normalize(settings.FlashcardMeaningLanguage),
            VocabularyCheckDirection = Enum.IsDefined(settings.VocabularyCheckDirection) ? settings.VocabularyCheckDirection : VocabularyCheckDirection.MeaningToWord,
            MeaningTranslations = translations
        };
    }

    /// <summary>The meaning of <paramref name="card"/> in <paramref name="languageCode"/>, or null when it has not been translated yet.</summary>
    public static string? MeaningIn(AppSettings settings, FlashcardItem card, string? languageCode)
    {
        var language = MeaningLanguages.Normalize(languageCode);
        if (language == MeaningLanguages.English)
            return card.Meaning;

        var phrase = Key(card);
        var source = CleanText(card.Meaning);
        return (settings.MeaningTranslations ?? [])
            .LastOrDefault(item => item is not null
                && string.Equals(item.Language, language, StringComparison.OrdinalIgnoreCase)
                && string.Equals(item.NormalizedPhrase, phrase, StringComparison.Ordinal)
                && string.Equals(CleanText(item.SourceMeaning), source, StringComparison.Ordinal))?.Text;
    }

    /// <summary>The cards whose meaning has no translation in <paramref name="languageCode"/> yet (none for English).</summary>
    public static IReadOnlyList<FlashcardItem> MissingTranslations(AppSettings settings, IEnumerable<FlashcardItem> cards, string? languageCode)
    {
        if (MeaningLanguages.Normalize(languageCode) == MeaningLanguages.English)
            return [];
        return cards
            .Where(card => !string.IsNullOrWhiteSpace(card.Meaning) && MeaningIn(settings, card, languageCode) is null)
            .GroupBy(Key)
            .Select(group => group.First())
            .ToList();
    }

    /// <summary>Saves translated meanings; empty ones are ignored.</summary>
    public static AppSettings AddTranslations(AppSettings settings, string? languageCode, IEnumerable<(FlashcardItem Card, string Text)> translations)
    {
        var language = MeaningLanguages.Normalize(languageCode);
        if (language == MeaningLanguages.English)
            return settings;

        var added = translations
            .Where(item => item.Card is not null && !string.IsNullOrWhiteSpace(item.Text))
            .Select(item => new MeaningTranslation(language, Key(item.Card), CleanText(item.Card.Meaning), CleanText(item.Text)))
            .ToList();
        if (added.Count == 0)
            return settings;
        return Normalize(settings with { MeaningTranslations = [.. settings.MeaningTranslations ?? [], .. added] });
    }

    /// <summary>
    /// The cards a check asks about: the ones the learner struggles with first (learning, then new, then the ones remembered
    /// longest ago), at most <paramref name="count"/>, in a shuffled order. Cards marked done are left out.
    /// </summary>
    public static IReadOnlyList<VocabularyCheckQuestion> PickQuestions(
        IEnumerable<FlashcardItem> cards,
        VocabularyCheckDirection direction,
        int count = DefaultQuestionCount,
        Random? random = null)
    {
        random ??= Random.Shared;
        var picked = cards
            .Where(card => card.IsReady && !card.IsMarkedDone && !string.IsNullOrWhiteSpace(card.Phrase) && !string.IsNullOrWhiteSpace(card.Meaning))
            .OrderBy(card => card.State switch
            {
                FlashcardLearningState.Learning => 0,
                FlashcardLearningState.New => 1,
                _ => 2
            })
            .ThenByDescending(card => card.AgainCount)
            .ThenBy(card => card.LastReviewedAt ?? DateTimeOffset.MinValue)
            .Take(Math.Max(1, count))
            .OrderBy(_ => random.Next())
            .ToList();

        return picked
            .Select((card, index) => new VocabularyCheckQuestion(card, direction switch
            {
                VocabularyCheckDirection.Mixed => index % 2 == 0 ? VocabularyCheckDirection.MeaningToWord : VocabularyCheckDirection.WordToMeaning,
                VocabularyCheckDirection.WordToMeaning => VocabularyCheckDirection.WordToMeaning,
                _ => VocabularyCheckDirection.MeaningToWord
            }))
            .ToList();
    }

    /// <summary>Checks a typed word: case, extra spaces and surrounding punctuation do not matter; a small typo is "close".</summary>
    public static WordAnswerResult CheckWord(string expected, string? answer)
    {
        var target = Comparable(expected);
        var typed = Comparable(answer);
        if (typed.Length == 0 || target.Length == 0)
            return WordAnswerResult.Wrong;
        if (string.Equals(typed, target, StringComparison.Ordinal))
            return WordAnswerResult.Correct;

        var allowed = target.Length >= 8 ? 2 : target.Length >= 4 ? 1 : 0;
        return allowed > 0 && Distance(typed, target) <= allowed ? WordAnswerResult.Close : WordAnswerResult.Wrong;
    }

    /// <summary>
    /// <paramref name="text"/> with the word (and forms that start with it, such as "universities" for "university"'s
    /// stem) replaced by a blank, so a meaning or example does not give the answer away.
    /// </summary>
    public static string MaskPhrase(string? text, string? phrase)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;
        var words = (phrase ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Select(word => word.Trim('.', ',', '!', '?', ';', ':', '"', '\'', '(', ')'))
            .Where(word => word.Length > 0)
            .ToList();
        if (words.Count == 0)
            return text.Trim();

        // Each word may end differently ("study" → "studies", "make" → "making"): keep its stem and allow any ending.
        var pattern = string.Join(@"\s+", words.Select(word =>
        {
            var stem = word.Length > 4 ? word[..^1] : word;
            return Regex.Escape(stem) + @"[\p{L}'’-]*";
        }));
        return Regex.Replace(text.Trim(), $@"(?<![\p{{L}}])(?:{pattern})", Blank, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    /// <summary>Progress for an answered question: right first time is remembered, right after a typo is hard, otherwise again.</summary>
    public static FlashcardRating RatingFor(bool correct, bool hadSecondTry) =>
        !correct ? FlashcardRating.Again : hadSecondTry ? FlashcardRating.Hard : FlashcardRating.Remembered;

    private static string Key(FlashcardItem card) =>
        VocabularyRules.NormalizePhrase(string.IsNullOrWhiteSpace(card.NormalizedPhrase) ? card.Phrase : card.NormalizedPhrase).ToLowerInvariant();

    private static string CleanText(string? text) => Spaces.Replace(text ?? string.Empty, " ").Trim();

    private static string Comparable(string? text)
    {
        var builder = new StringBuilder();
        foreach (var c in (text ?? string.Empty).Normalize(NormalizationForm.FormC).ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(c))
                builder.Append(c);
            else if (c is '\'' or '’')
                builder.Append('\'');
            else if (char.IsWhiteSpace(c) || c is '-' or '–')
                builder.Append(' ');
        }

        return Spaces.Replace(builder.ToString(), " ").Trim();
    }

    private static int Distance(string a, string b)
    {
        var previous = new int[b.Length + 1];
        var current = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++)
            previous[j] = j;
        for (var i = 1; i <= a.Length; i++)
        {
            current[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + cost);
            }

            (previous, current) = (current, previous);
        }

        return previous[b.Length];
    }
}
