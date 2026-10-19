namespace PteFloatingSentence.Core;

/// <summary>Which words an AI correction changed in a sentence, so the learner can see them ("Card → car").</summary>
public static class SentenceCorrections
{
    private static readonly char[] Punctuation = ['.', ',', '!', '?', ';', ':', '"', '\'', '“', '”', '‘', '’', '(', ')', '…'];

    /// <summary>
    /// The changed words, in order, as "old → new", "old → (removed)" or "+ new". Punctuation-only changes are left out;
    /// a change of capital letters counts.
    /// </summary>
    public static IReadOnlyList<string> WordChanges(string? original, string? corrected)
    {
        var a = Words(original);
        var b = Words(corrected);

        // Longest common subsequence of the words; what is left between matches is a change.
        var lengths = new int[a.Length + 1, b.Length + 1];
        for (var i = a.Length - 1; i >= 0; i--)
            for (var j = b.Length - 1; j >= 0; j--)
                lengths[i, j] = a[i] == b[j] ? lengths[i + 1, j + 1] + 1 : Math.Max(lengths[i + 1, j], lengths[i, j + 1]);

        var changes = new List<string>();
        var removed = new List<string>();
        var added = new List<string>();
        void Flush()
        {
            if (removed.Count > 0 && added.Count > 0)
                changes.Add($"{string.Join(' ', removed)} → {string.Join(' ', added)}");
            else if (removed.Count > 0)
                changes.Add($"{string.Join(' ', removed)} → (removed)");
            else if (added.Count > 0)
                changes.Add($"+ {string.Join(' ', added)}");
            removed.Clear();
            added.Clear();
        }

        int x = 0, y = 0;
        while (x < a.Length || y < b.Length)
        {
            if (x < a.Length && y < b.Length && a[x] == b[y])
            {
                Flush();
                x++;
                y++;
            }
            else if (y >= b.Length || (x < a.Length && lengths[x + 1, y] >= lengths[x, y + 1]))
            {
                removed.Add(a[x++]);
            }
            else
            {
                added.Add(b[y++]);
            }
        }

        Flush();
        return changes;
    }

    private static string[] Words(string? text) =>
        (text ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Select(word => word.Trim(Punctuation))
            .Where(word => word.Length > 0)
            .ToArray();
}
