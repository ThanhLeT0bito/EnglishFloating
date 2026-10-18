using System.Text.RegularExpressions;

namespace PteFloatingSentence.Core;

/// <summary>
/// Splits pasted text into sentences for a study list, without AI: one sentence per line, and several sentences on a
/// line at ". ", "? ", "! " or "… ". List numbers and bullets at the start ("1.", "2)", "-", "•") are dropped. A dot after a
/// common abbreviation (Mr., Dr., e.g.) or an initial ("J. K.") does not end a sentence, and "3.5" or "U.S.A." stay
/// whole because no space follows their dots.
/// </summary>
public static class SentenceSplitter
{
    public const int MaxSentences = 100;

    private static readonly HashSet<string> Abbreviations = new(StringComparer.OrdinalIgnoreCase)
    {
        "mr", "mrs", "ms", "dr", "prof", "sr", "jr", "st", "mt", "vs", "e.g", "i.e", "a.m", "p.m", "u.s", "u.k",
        "no", "approx", "dept", "inc", "ltd", "co", "fig", "jan", "feb", "mar", "apr", "jun", "jul", "aug", "sep",
        "sept", "oct", "nov", "dec"
    };

    private static readonly Regex ListMarker = new(@"^\s*(?:\(?\d{1,3}[.)]|[-•*–—])\s+", RegexOptions.Compiled);
    private static readonly Regex Spaces = new(@"\s+", RegexOptions.Compiled);

    /// <summary>The sentences in <paramref name="text"/>, in order, cleaned up; at most <see cref="MaxSentences"/>.</summary>
    public static IReadOnlyList<string> Split(string? text)
    {
        var sentences = new List<string>();
        foreach (var rawLine in (text ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            var line = rawLine.Trim();
            var start = 0;
            for (var i = 0; i < line.Length; i++)
            {
                var c = line[i];
                if (c is not ('.' or '!' or '?' or '…'))
                    continue;

                // Take "?!", "..." and closing quotes or brackets with the sentence.
                var end = i + 1;
                while (end < line.Length && line[end] is '.' or '!' or '?' or '…' or '"' or '\'' or '”' or '’' or ')' or ']')
                    end++;

                // No space after it (3.5, U.S.A., an email) or the end of the line: not a break inside the line.
                if (end >= line.Length || !char.IsWhiteSpace(line[end]))
                {
                    i = end - 1;
                    continue;
                }

                if (c == '.' && end == i + 1 && IsAbbreviation(line, start, i))
                    continue;

                Add(sentences, line[start..end]);
                start = end;
                i = end - 1;
            }

            Add(sentences, line[start..]);
            if (sentences.Count >= MaxSentences)
                break;
        }

        return sentences.Count <= MaxSentences ? sentences : sentences.Take(MaxSentences).ToList();
    }

    /// <summary>True when the word before the dot at <paramref name="dot"/> is an abbreviation, an initial or a list number.</summary>
    private static bool IsAbbreviation(string line, int start, int dot)
    {
        var wordStart = dot;
        while (wordStart > start && !char.IsWhiteSpace(line[wordStart - 1]))
            wordStart--;
        var word = line[wordStart..dot].Trim('(', '"', '\'', '“', '‘');
        if (word.Length == 0)
            return false;
        // "J. K. Rowling": a single capital letter is an initial.
        if (word.Length == 1 && char.IsUpper(word[0]))
            return true;
        // "1. Stock prices…" at the start: a list number, removed later.
        if (wordStart == start && word.Length <= 3 && word.All(char.IsDigit))
            return true;
        // "U.S.A." or "e.g.": short letter groups joined by dots.
        if (word.Contains('.') && word.Split('.').All(part => part.Length is > 0 and <= 2 && part.All(char.IsLetter)))
            return true;
        return Abbreviations.Contains(word);
    }

    private static void Add(List<string> sentences, string text)
    {
        var sentence = Spaces.Replace(ListMarker.Replace(text, string.Empty), " ").Trim();
        // A stray number or bullet left on its own is not a sentence.
        if (sentence.Any(char.IsLetter))
            sentences.Add(sentence);
    }
}
