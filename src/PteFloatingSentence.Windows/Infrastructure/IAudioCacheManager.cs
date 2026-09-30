using System.IO;

namespace PteFloatingSentence.Windows.Infrastructure;

public interface IAudioCacheManager
{
    string GetCacheFilePath(string text, string voice, double speed);

    bool TryGetCachedAudio(string text, string voice, double speed, out string filePath);

    Task SaveAudioAsync(string text, string voice, double speed, Stream audioStream, CancellationToken cancellationToken = default);

    long GetTotalCacheSizeBytes();

    void ClearCache();

    void PruneToLimit(long maxSizeBytes);
}
