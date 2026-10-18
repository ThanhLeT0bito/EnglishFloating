using System.Text;

namespace PteFloatingSentence.Core;

/// <summary>Outcome of retyping the corrected sentence. <see cref="WordIndex"/> is the first differing word (0-based).</summary>
public sealed record RetypeResult(bool IsMatch, int WordIndex, string? Expected, string? Typed)
{
    public static RetypeResult Match { get; } = new(true, -1, null, null);
}

public enum DictationWordStatus
{
    Correct = 0,
    Misspelled = 1,
    Wrong = 2,
    Missing = 3,
    Extra = 4
}

/// <summary>One aligned word. <see cref="Expected"/> is null for an extra word; <see cref="Typed"/> is null for a missing word.</summary>
public sealed record DictationWord(string? Expected, string? Typed, DictationWordStatus Status);

/// <summary>PTE-style dictation result: one point for each reference word typed and spelled correctly.</summary>
public sealed record DictationResult(int Correct, int Total, IReadOnlyList<DictationWord> Words)
{
    public int Score => Total == 0 ? 0 : (int)Math.Round(100.0 * Correct / Total, MidpointRounding.AwayFromZero);
}

/// <summary>Word-level comparisons that ignore case and punctuation; no AI involved.</summary>
public static class WritingTextMatch
{
    /// <summary>
    /// Splits text into lowercase words of letters, digits, apostrophes and inner hyphens. Curly apostrophes become
    /// straight ones; a plural possessive keeps its apostrophe ("students'") but a closing quote does not ("'yes'");
    /// a thousands comma joins the number ("1,000" is "1000"); "." and ":" between digits become one "." so "10:30"
    /// matches "10.30" and "3.5" stays different from "35"; all other punctuation is ignored.
    /// </summary>
    public static IReadOnlyList<string> Words(string? text)
    {
        var words = new List<string>();
        var current = new StringBuilder();
        var source = text ?? string.Empty;
        var quoted = false;
        for (var i = 0; i < source.Length; i++)
        {
            var c = Apostrophe(source[i]);
            if (char.IsLetterOrDigit(c) || ((c == '\'' || c == '-') && current.Length > 0))
            {
                if (current.Length == 0)
                    quoted = i > 0 && Apostrophe(source[i - 1]) == '\'';
                current.Append(char.ToLowerInvariant(c));
                continue;
            }

            if (current.Length > 0 && char.IsDigit(current[^1]) && i + 1 < source.Length && char.IsDigit(source[i + 1]))
            {
                // 1,000: a thousands separator (exactly three digits follow) is dropped.
                if (c == ',' && IsDigitGroup(source, i + 1))
                    continue;
                // 3.5 · 10:30: one standard separator inside the number.
                if (c is '.' or ':')
                {
                    current.Append('.');
                    continue;
                }
            }

            Flush(current, words, quoted);
            quoted = false;
        }

        Flush(current, words, quoted);
        return words;
    }

    private static bool IsDigitGroup(string source, int start) =>
        start + 3 <= source.Length
        && char.IsDigit(source[start]) && char.IsDigit(source[start + 1]) && char.IsDigit(source[start + 2])
        && (start + 3 == source.Length || !char.IsDigit(source[start + 3]));

    private static char Apostrophe(char c) => c is '’' or '‘' or '`' ? '\'' : c;

    private static void Flush(StringBuilder current, List<string> words, bool quoted)
    {
        var word = current.ToString().TrimEnd('-');
        current.Clear();
        // A closing quote is not part of the word ('yes', ‘crisis’), but a plural possessive's apostrophe is ("students'").
        while (word.EndsWith('\'') && (quoted || !(word.Length > 2 && word[^2] == 's' && !word.EndsWith("''", StringComparison.Ordinal))))
            word = word[..^1].TrimEnd('-');
        if (word.Length > 0)
            words.Add(word);
    }

    /// <summary>True when both texts have the same words, ignoring case and punctuation.</summary>
    public static bool SameWords(string? a, string? b) => Words(a).SequenceEqual(Words(b), StringComparer.Ordinal);

    /// <summary>Compares the learner's retyped sentence with the corrected one and reports the first difference.</summary>
    public static RetypeResult CompareRetype(string? expected, string? typed)
    {
        var want = Words(expected);
        var got = Words(typed);
        for (var i = 0; i < Math.Max(want.Count, got.Count); i++)
        {
            var w = i < want.Count ? want[i] : null;
            var g = i < got.Count ? got[i] : null;
            if (!string.Equals(w, g, StringComparison.Ordinal))
                return new RetypeResult(false, i, w, g);
        }

        return RetypeResult.Match;
    }

    /// <summary>
    /// Aligns the typed words with the reference (minimum edit distance on words) and scores them PTE style.
    /// A substituted word is "misspelled" when it is close to the reference word, otherwise "wrong".
    /// </summary>
    public static DictationResult ScoreDictation(string? reference, string? typed)
    {
        var want = Words(reference);
        var got = Words(typed);
        var n = want.Count;
        var m = got.Count;
        var cost = new int[n + 1, m + 1];
        for (var i = 0; i <= n; i++)
            cost[i, 0] = i;
        for (var j = 0; j <= m; j++)
            cost[0, j] = j;
        for (var i = 1; i <= n; i++)
        {
            for (var j = 1; j <= m; j++)
            {
                var same = SameSpoken(want[i - 1], got[j - 1]);
                // A close misspelling is cheaper than a missing + extra pair, so it stays aligned.
                var substitute = same ? 0 : IsCloseSpelling(want[i - 1], got[j - 1]) ? 1 : 2;
                cost[i, j] = Math.Min(Math.Min(cost[i - 1, j] + 1, cost[i, j - 1] + 1), cost[i - 1, j - 1] + substitute);
            }
        }

        var words = new List<DictationWord>();
        int a = n, b = m;
        while (a > 0 || b > 0)
        {
            if (a > 0 && b > 0)
            {
                var same = SameSpoken(want[a - 1], got[b - 1]);
                var close = !same && IsCloseSpelling(want[a - 1], got[b - 1]);
                var substitute = same ? 0 : close ? 1 : 2;
                if (cost[a, b] == cost[a - 1, b - 1] + substitute)
                {
                    var status = same ? DictationWordStatus.Correct : close ? DictationWordStatus.Misspelled : DictationWordStatus.Wrong;
                    words.Add(new DictationWord(want[a - 1], got[b - 1], status));
                    a--;
                    b--;
                    continue;
                }
            }

            if (a > 0 && cost[a, b] == cost[a - 1, b] + 1)
            {
                words.Add(new DictationWord(want[a - 1], null, DictationWordStatus.Missing));
                a--;
            }
            else
            {
                words.Add(new DictationWord(null, got[b - 1], DictationWordStatus.Extra));
                b--;
            }
        }

        words.Reverse();
        return new DictationResult(words.Count(word => word.Status == DictationWordStatus.Correct), n, words);
    }

    /// <summary>Same word as heard: a possessive apostrophe cannot be heard, so "students" counts for "students'".</summary>
    private static bool SameSpoken(string expected, string typed) =>
        string.Equals(expected, typed, StringComparison.Ordinal)
        || string.Equals(expected.TrimEnd('\''), typed.TrimEnd('\''), StringComparison.Ordinal);

    /// <summary>True when <paramref name="typed"/> looks like a misspelling of <paramref name="expected"/>.</summary>
    public static bool IsCloseSpelling(string expected, string typed)
    {
        if (expected.Length == 0 || typed.Length == 0)
            return false;
        var allowed = Math.Max(1, Math.Max(expected.Length, typed.Length) / 3);
        return Math.Abs(expected.Length - typed.Length) <= allowed && Levenshtein(expected, typed) <= allowed;
    }

    private static int Levenshtein(string s, string t)
    {
        var previous = new int[t.Length + 1];
        var current = new int[t.Length + 1];
        for (var j = 0; j <= t.Length; j++)
            previous[j] = j;
        for (var i = 1; i <= s.Length; i++)
        {
            current[0] = i;
            for (var j = 1; j <= t.Length; j++)
            {
                var substitution = previous[j - 1] + (s[i - 1] == t[j - 1] ? 0 : 1);
                current[j] = Math.Min(Math.Min(previous[j] + 1, current[j - 1] + 1), substitution);
            }

            (previous, current) = (current, previous);
        }

        return previous[t.Length];
    }

    /// <summary>
    /// Turns a dictation result into feedback so it is shown, retyped and recorded like a translation check.
    /// </summary>
    public static WritingFeedback DictationFeedback(string reference, DictationResult result, WritingExplanationLanguage language)
    {
        var vi = language == WritingExplanationLanguage.Vietnamese;
        var issues = new List<WritingIssue>();
        foreach (var word in result.Words)
        {
            switch (word.Status)
            {
                case DictationWordStatus.Misspelled:
                    issues.Add(new WritingIssue(WritingIssueType.Spelling, word.Typed!, word.Expected!,
                        vi ? "Sai chính tả." : "Spelling mistake."));
                    break;
                case DictationWordStatus.Wrong:
                    issues.Add(new WritingIssue(WritingIssueType.Vocabulary, word.Typed!, word.Expected!,
                        vi ? "Nghe nhầm thành từ khác." : "A different word was said here.")
                    { Tag = WritingSkillTag.WordChoice });
                    break;
                case DictationWordStatus.Missing:
                    issues.Add(new WritingIssue(WritingIssueType.MissingMeaning, string.Empty, word.Expected!,
                        vi ? "Thiếu từ này." : "This word is missing."));
                    break;
                case DictationWordStatus.Extra:
                    issues.Add(new WritingIssue(WritingIssueType.ExtraMeaning, word.Typed!, string.Empty,
                        vi ? "Từ này không có trong câu." : "This word was not in the sentence."));
                    break;
            }
        }

        var summary = vi
            ? $"Đúng {result.Correct}/{result.Total} từ."
            : $"{result.Correct} of {result.Total} words correct.";
        return WritingFeedbackRules.Normalize(new WritingFeedback(result.Score, WritingVerdict.NeedsWork, reference, null, summary, issues));
    }
}
