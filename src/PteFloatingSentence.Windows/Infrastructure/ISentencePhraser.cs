namespace PteFloatingSentence.Windows.Infrastructure;

public interface ISentencePhraser
{
    Task<IReadOnlyList<string>> SuggestAsync(string sentence, CancellationToken cancellationToken = default);
}
