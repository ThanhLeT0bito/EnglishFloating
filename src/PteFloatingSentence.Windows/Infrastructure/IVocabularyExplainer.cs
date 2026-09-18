namespace PteFloatingSentence.Windows.Infrastructure;

public sealed record VocabularyExplanation(string Meaning, string Example, string PronunciationIpa);

public interface IVocabularyExplainer
{
    Task<VocabularyExplanation> ExplainAsync(string phrase, string sourceSentence = "", CancellationToken cancellationToken = default);
}
