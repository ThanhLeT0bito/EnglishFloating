using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using PteFloatingSentence.Core;

namespace PteFloatingSentence.Windows;

public readonly record struct RenderSignature(string Value)
{
    public static RenderSignature Create(
        string text,
        double fontSize,
        string textColor,
        double backgroundOpacity,
        IReadOnlyList<VocabularyItem> vocabulary,
        IReadOnlyList<int>? phraseBreakAfterWordIndices = null)
    {
        var builder = new StringBuilder(text.Length + vocabulary.Count * 80);
        builder.Append(text).Append('|')
            .Append(fontSize.ToString("R", CultureInfo.InvariantCulture)).Append('|')
            .Append(textColor).Append('|')
            .Append(backgroundOpacity.ToString("R", CultureInfo.InvariantCulture));

        if (phraseBreakAfterWordIndices is { Count: > 0 })
            builder.Append("|phrasing:").AppendJoin(',', phraseBreakAfterWordIndices);

        foreach (var item in vocabulary)
        {
            builder.Append('|').Append(item.Id).Append(':').Append(item.Phrase)
                .Append(':').Append(item.Status).Append(':').Append(item.IsHidden)
                .Append(':').Append(item.Meaning).Append(':').Append(item.Example)
                .Append(':').Append(item.PronunciationIpa).Append(':').Append(item.LastError);
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()));
        return new RenderSignature(Convert.ToHexString(hash));
    }
}
