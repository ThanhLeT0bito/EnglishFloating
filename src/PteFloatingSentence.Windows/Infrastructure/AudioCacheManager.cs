using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using PteFloatingSentence.Core;

namespace PteFloatingSentence.Windows.Infrastructure;

public sealed class AudioCacheManager : IAudioCacheManager
{
    public const long DefaultMaxCacheSizeBytes = 100 * 1024 * 1024; // 100 MB

    private readonly string _cacheDirectory;
    private readonly long _maxSizeBytes;

    public AudioCacheManager(string? cacheDirectory = null, long maxSizeBytes = DefaultMaxCacheSizeBytes)
    {
        _cacheDirectory = !string.IsNullOrWhiteSpace(cacheDirectory)
            ? cacheDirectory
            : Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PteFloatingSentence",
                "audio_cache");

        _maxSizeBytes = maxSizeBytes > 0 ? maxSizeBytes : DefaultMaxCacheSizeBytes;
    }

    public string CacheDirectory => _cacheDirectory;

    public long MaxSizeBytes => _maxSizeBytes;

    public string GetCacheFilePath(string text, string voice, double speed)
    {
        var normalizedText = VocabularyRules.NormalizePhrase(text);
        var normalizedVoice = (voice ?? string.Empty).Trim();
        var normalizedSpeed = speed.ToString("0.0#", CultureInfo.InvariantCulture);

        var rawKey = $"{normalizedText}_{normalizedVoice}_{normalizedSpeed}";
        var hashBytes = MD5.HashData(Encoding.UTF8.GetBytes(rawKey));
        var hashHex = Convert.ToHexString(hashBytes).ToLowerInvariant();

        return Path.Combine(_cacheDirectory, $"{hashHex}.mp3");
    }

    public bool TryGetCachedAudio(string text, string voice, double speed, out string filePath)
    {
        filePath = GetCacheFilePath(text, voice, speed);

        try
        {
            var fileInfo = new FileInfo(filePath);
            if (!fileInfo.Exists || fileInfo.Length <= 0)
            {
                return false;
            }

            try
            {
                File.SetLastAccessTimeUtc(filePath, DateTime.UtcNow);
            }
            catch
            {
                // Best effort: do not fail cache retrieval if timestamp update fails
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    public async Task SaveAudioAsync(
        string text,
        string voice,
        double speed,
        Stream audioStream,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(audioStream);
        cancellationToken.ThrowIfCancellationRequested();

        Directory.CreateDirectory(_cacheDirectory);

        var targetFilePath = GetCacheFilePath(text, voice, speed);
        var tempFilePath = Path.Combine(_cacheDirectory, $"{Guid.NewGuid():N}.tmp");

        try
        {
            await using (var fileStream = new FileStream(
                tempFilePath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 81920,
                useAsync: true))
            {
                await audioStream.CopyToAsync(fileStream, cancellationToken);
            }

            File.Move(tempFilePath, targetFilePath, overwrite: true);

            try
            {
                File.SetLastAccessTimeUtc(targetFilePath, DateTime.UtcNow);
            }
            catch
            {
                // Best effort
            }

            PruneToLimit(_maxSizeBytes);
        }
        finally
        {
            if (File.Exists(tempFilePath))
            {
                try
                {
                    File.Delete(tempFilePath);
                }
                catch
                {
                    // Best effort cleanup
                }
            }
        }
    }

    public long GetTotalCacheSizeBytes()
    {
        try
        {
            if (!Directory.Exists(_cacheDirectory))
                return 0;

            var directoryInfo = new DirectoryInfo(_cacheDirectory);
            long total = 0;
            foreach (var file in directoryInfo.EnumerateFiles("*.mp3"))
            {
                try
                {
                    total += file.Length;
                }
                catch
                {
                    // Ignore files concurrently deleted or inaccessible
                }
            }

            return total;
        }
        catch
        {
            return 0;
        }
    }

    public void ClearCache()
    {
        try
        {
            if (!Directory.Exists(_cacheDirectory))
                return;

            var directoryInfo = new DirectoryInfo(_cacheDirectory);
            foreach (var file in directoryInfo.EnumerateFiles())
            {
                try
                {
                    file.Delete();
                }
                catch
                {
                    // Best effort for individual files
                }
            }
        }
        catch
        {
            // Best effort
        }
    }

    public void PruneToLimit(long maxSizeBytes)
    {
        try
        {
            if (!Directory.Exists(_cacheDirectory))
                return;

            if (maxSizeBytes <= 0)
            {
                ClearCache();
                return;
            }

            var directoryInfo = new DirectoryInfo(_cacheDirectory);
            var files = directoryInfo.EnumerateFiles("*.mp3").ToList();
            long totalSize = 0;
            foreach (var file in files)
            {
                try
                {
                    totalSize += file.Length;
                }
                catch
                {
                    // Skip if unreadable
                }
            }

            if (totalSize <= maxSizeBytes)
                return;

            var sortedFiles = files
                .OrderBy(f => f.LastAccessTimeUtc)
                .ThenBy(f => f.LastWriteTimeUtc);

            foreach (var file in sortedFiles)
            {
                if (totalSize <= maxSizeBytes)
                    break;

                try
                {
                    var fileLength = file.Length;
                    file.Delete();
                    totalSize -= fileLength;
                }
                catch
                {
                    // If file is locked or cannot be deleted, proceed to next
                }
            }
        }
        catch
        {
            // Best effort
        }
    }
}
