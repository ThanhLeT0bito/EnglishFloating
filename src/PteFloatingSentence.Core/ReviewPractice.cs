namespace PteFloatingSentence.Core;

public sealed record ReviewToken(
    string SourceText,
    string DisplayText,
    bool IsHidden,
    int SourceIndex);

public sealed record ReviewSentence(
    Guid SentenceId,
    string OriginalText,
    IReadOnlyList<ReviewToken> Tokens,
    IReadOnlyList<int> HiddenTokenIndexes)
{
    public string DisplayText => string.Join(" ", Tokens.Select(t => t.DisplayText));
}

public sealed record ReviewAnswerResult(
    bool IsCorrect,
    bool IsComplete,
    int NextHiddenTokenPosition,
    string? Error);

public static class ReviewPracticeRules
{
    public static bool IsCorrectDictation(string expected, string? answer)
    {
        static string Normalize(string? text) => string.Join(" ",
            (text ?? string.Empty)
                .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
                .Select(TrimOuterPunctuation)
                .Where(word => word.Length > 0));

        var normalizedExpected = Normalize(expected);
        return normalizedExpected.Length > 0
            && string.Equals(normalizedExpected, Normalize(answer), StringComparison.OrdinalIgnoreCase);
    }

    private static string TrimOuterPunctuation(string word)
    {
        var start = 0;
        var end = word.Length - 1;
        while (start <= end && char.IsPunctuation(word[start])) start++;
        while (end >= start && char.IsPunctuation(word[end])) end--;
        return word[start..(end + 1)];
    }

    public static ReviewSentence CreateProjection(StudySentence sentence, int? seed = null)
    {
        ArgumentNullException.ThrowIfNull(sentence);

        var rawWords = sentence.Text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (rawWords.Length == 0)
        {
            return new ReviewSentence(
                sentence.Id,
                sentence.Text,
                Array.Empty<ReviewToken>(),
                Array.Empty<int>());
        }

        var resolvedSeed = seed ?? BitConverter.ToInt32(sentence.Id.ToByteArray(), 0);
        var hiddenIndexes = SelectHiddenIndexes(rawWords, resolvedSeed);
        var hiddenSet = new HashSet<int>(hiddenIndexes);

        var tokens = new List<ReviewToken>(rawWords.Length);
        for (var i = 0; i < rawWords.Length; i++)
        {
            var word = rawWords[i];
            var isHidden = hiddenSet.Contains(i);
            var displayText = isHidden ? "_" : word;
            tokens.Add(new ReviewToken(word, displayText, isHidden, i));
        }

        return new ReviewSentence(sentence.Id, sentence.Text, tokens, hiddenIndexes);
    }

    public static ReviewAnswerResult CheckAnswer(ReviewSentence review, int hiddenPosition, string answer)
    {
        ArgumentNullException.ThrowIfNull(review);

        if (review.HiddenTokenIndexes.Count == 0)
        {
            return new ReviewAnswerResult(true, true, 0, null);
        }

        if (hiddenPosition < 0 || hiddenPosition >= review.HiddenTokenIndexes.Count)
        {
            return new ReviewAnswerResult(false, false, hiddenPosition, "Invalid position.");
        }

        var tokenIndex = review.HiddenTokenIndexes[hiddenPosition];
        var expectedToken = review.Tokens[tokenIndex];

        var normalizedExpected = TrimOuterPunctuation(expectedToken.SourceText.Trim());
        var normalizedActual = TrimOuterPunctuation((answer ?? string.Empty).Trim());

        if (string.Equals(normalizedExpected, normalizedActual, StringComparison.OrdinalIgnoreCase))
        {
            var isLast = hiddenPosition >= review.HiddenTokenIndexes.Count - 1;
            var nextPosition = isLast ? hiddenPosition : hiddenPosition + 1;
            return new ReviewAnswerResult(true, isLast, nextPosition, null);
        }

        return new ReviewAnswerResult(false, false, hiddenPosition, "Try again.");
    }

    private static IReadOnlyList<int> SelectHiddenIndexes(string[] words, int seed)
    {
        if (words.Length == 1)
        {
            return words[0].Any(char.IsLetterOrDigit) ? [0] : Array.Empty<int>();
        }

        // Identify candidate tokens that contain at least one letter or digit
        // Leave the final word visible when possible
        var candidateIndexes = new List<int>();
        var lastWordIndex = words.Length - 1;

        for (var i = 0; i < lastWordIndex; i++)
        {
            if (words[i].Any(char.IsLetterOrDigit))
            {
                candidateIndexes.Add(i);
            }
        }

        if (candidateIndexes.Count == 0)
        {
            return words[lastWordIndex].Any(char.IsLetterOrDigit)
                ? [lastWordIndex]
                : Array.Empty<int>();
        }

        int targetCount;
        if (words.Length <= 3)
        {
            targetCount = 1;
        }
        else if (words.Length <= 8)
        {
            targetCount = Math.Clamp((int)Math.Round(words.Length / 2.0, MidpointRounding.AwayFromZero), 1, words.Length - 1);
        }
        else
        {
            targetCount = Math.Clamp((int)Math.Round(words.Length * 0.45, MidpointRounding.AwayFromZero), 2, words.Length - 1);
        }

        targetCount = Math.Min(targetCount, candidateIndexes.Count);

        var rng = new Random(seed);
        // Deterministic shuffle of candidate indices
        var shuffled = candidateIndexes.OrderBy(_ => rng.Next()).Take(targetCount).OrderBy(idx => idx).ToList();
        return shuffled;
    }
}
