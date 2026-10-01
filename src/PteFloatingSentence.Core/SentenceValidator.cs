namespace PteFloatingSentence.Core;

public readonly record struct ValidationResult(bool IsValid, string? Error);

public static class SentenceValidator
{
    public static ValidationResult Validate(string? sentence)
    {
        if (string.IsNullOrWhiteSpace(sentence) || !sentence.Any(char.IsLetterOrDigit))
            return new(false, "Enter a sentence.");

        var words = sentence.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return words.Length <= 20
            ? new(true, null)
            : new(false, "Use 20 words or fewer.");
    }
}
