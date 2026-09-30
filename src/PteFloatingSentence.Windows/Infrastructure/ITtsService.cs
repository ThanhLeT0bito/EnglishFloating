using System.IO;

namespace PteFloatingSentence.Windows.Infrastructure;

public interface ITtsService
{
    Task<Stream> SynthesizeSpeechAsync(string text, string voice, double speed, CancellationToken cancellationToken = default);
}
