using System.IO;
using System.Text;
using FluentAssertions;
using PteFloatingSentence.Windows.Infrastructure;
using Xunit;

namespace PteFloatingSentence.Windows.Tests;

public class AudioCacheManagerTests : IDisposable
{
    private readonly string _testDir;

    public AudioCacheManagerTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "AudioCacheManagerTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
        {
            try
            {
                Directory.Delete(_testDir, recursive: true);
            }
            catch
            {
                // Best effort cleanup in test disposal
            }
        }
    }

    [Fact]
    public void GetCacheFilePath_ReturnsConsistentNormalizedMd5Path()
    {
        var cache = new AudioCacheManager(_testDir);
        var p1 = cache.GetCacheFilePath("Hello world", "en-US-JennyNeural", 1.0);
        var p2 = cache.GetCacheFilePath("Hello world", "en-US-JennyNeural", 1.0);
        var p3 = cache.GetCacheFilePath("Hello world", "en-US-JennyNeural", 1.1);
        var p4 = cache.GetCacheFilePath("  Hello   world  ", "en-US-JennyNeural", 1.0);
        var p5 = cache.GetCacheFilePath("Hello world", "en-US-GuyNeural", 1.0);

        p1.Should().Be(p2);
        p1.Should().NotBe(p3);
        p1.Should().Be(p4);
        p1.Should().NotBe(p5);
        p1.Should().EndWith(".mp3");
        p1.Should().StartWith(_testDir);
    }

    [Fact]
    public void TryGetCachedAudio_WhenFileDoesNotExist_ReturnsFalse()
    {
        var cache = new AudioCacheManager(_testDir);

        var found = cache.TryGetCachedAudio("Unknown sentence", "en-US-JennyNeural", 1.0, out var filePath);

        found.Should().BeFalse();
        filePath.Should().EndWith(".mp3");
        File.Exists(filePath).Should().BeFalse();
    }

    [Fact]
    public async Task TryGetCachedAudio_WhenFileExistsAndNotEmpty_ReturnsTrue()
    {
        var cache = new AudioCacheManager(_testDir);
        var sampleBytes = Encoding.UTF8.GetBytes("audio-payload");
        using var stream = new MemoryStream(sampleBytes);

        await cache.SaveAudioAsync("Cached sentence", "en-US-JennyNeural", 1.0, stream, CancellationToken.None);

        var found = cache.TryGetCachedAudio("Cached sentence", "en-US-JennyNeural", 1.0, out var filePath);

        found.Should().BeTrue();
        File.Exists(filePath).Should().BeTrue();
        (new FileInfo(filePath).Length).Should().Be(sampleBytes.Length);
    }

    [Fact]
    public void TryGetCachedAudio_WhenFileIsEmpty_ReturnsFalse()
    {
        var cache = new AudioCacheManager(_testDir);
        var expectedPath = cache.GetCacheFilePath("Empty sentence", "en-US-JennyNeural", 1.0);
        File.WriteAllBytes(expectedPath, []);

        var found = cache.TryGetCachedAudio("Empty sentence", "en-US-JennyNeural", 1.0, out var filePath);

        found.Should().BeFalse();
        filePath.Should().Be(expectedPath);
    }

    [Fact]
    public async Task SaveAudioAsync_WritesAudioStreamAtomically()
    {
        var cache = new AudioCacheManager(_testDir);
        var sampleData = Encoding.UTF8.GetBytes("fake mp3 data payload");
        using var stream = new MemoryStream(sampleData);

        await cache.SaveAudioAsync("Atomic sentence", "en-US-JennyNeural", 1.0, stream, CancellationToken.None);

        var filePath = cache.GetCacheFilePath("Atomic sentence", "en-US-JennyNeural", 1.0);
        File.Exists(filePath).Should().BeTrue();
        var written = await File.ReadAllBytesAsync(filePath);
        written.Should().Equal(sampleData);

        // Verify no leftover .tmp files
        Directory.GetFiles(_testDir, "*.tmp").Should().BeEmpty();
    }

    [Fact]
    public async Task SaveAudioAsync_WithCancellation_CleansUpTempFile()
    {
        var cache = new AudioCacheManager(_testDir);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("should not be saved"));

        var act = () => cache.SaveAudioAsync("Cancelled sentence", "en-US-JennyNeural", 1.0, stream, cts.Token);

        await FluentActions.Awaiting(act).Should().ThrowAsync<OperationCanceledException>();

        var filePath = cache.GetCacheFilePath("Cancelled sentence", "en-US-JennyNeural", 1.0);
        File.Exists(filePath).Should().BeFalse();
        Directory.GetFiles(_testDir, "*.tmp").Should().BeEmpty();
    }

    [Fact]
    public async Task SaveAudioAsync_PrunesOldestWhenExceedingLimit()
    {
        // Limit: 250 bytes. Each file: 100 bytes.
        // File 1 saved (total: 100 bytes)
        // File 2 saved (total: 200 bytes)
        // File 3 saved (total: 300 bytes -> exceeds 250 -> File 1 pruned, total: 200 bytes)
        var cache = new AudioCacheManager(_testDir, maxSizeBytes: 250);

        var data1 = new byte[100];
        Array.Fill(data1, (byte)1);
        using (var s1 = new MemoryStream(data1))
        {
            await cache.SaveAudioAsync("Sentence 1", "en-US-JennyNeural", 1.0, s1, CancellationToken.None);
        }

        // Ensure distinct access time
        var path1 = cache.GetCacheFilePath("Sentence 1", "en-US-JennyNeural", 1.0);
        File.SetLastAccessTimeUtc(path1, DateTime.UtcNow.AddMinutes(-10));

        var data2 = new byte[100];
        Array.Fill(data2, (byte)2);
        using (var s2 = new MemoryStream(data2))
        {
            await cache.SaveAudioAsync("Sentence 2", "en-US-JennyNeural", 1.0, s2, CancellationToken.None);
        }
        var path2 = cache.GetCacheFilePath("Sentence 2", "en-US-JennyNeural", 1.0);
        File.SetLastAccessTimeUtc(path2, DateTime.UtcNow.AddMinutes(-5));

        var data3 = new byte[100];
        Array.Fill(data3, (byte)3);
        using (var s3 = new MemoryStream(data3))
        {
            await cache.SaveAudioAsync("Sentence 3", "en-US-JennyNeural", 1.0, s3, CancellationToken.None);
        }
        var path3 = cache.GetCacheFilePath("Sentence 3", "en-US-JennyNeural", 1.0);

        cache.TryGetCachedAudio("Sentence 1", "en-US-JennyNeural", 1.0, out _).Should().BeFalse();
        cache.TryGetCachedAudio("Sentence 2", "en-US-JennyNeural", 1.0, out _).Should().BeTrue();
        cache.TryGetCachedAudio("Sentence 3", "en-US-JennyNeural", 1.0, out _).Should().BeTrue();

        cache.GetTotalCacheSizeBytes().Should().Be(200);
    }

    [Fact]
    public async Task GetTotalCacheSizeBytes_ReturnsSumOfCachedFiles()
    {
        var cache = new AudioCacheManager(_testDir);
        cache.GetTotalCacheSizeBytes().Should().Be(0);

        var payload1 = new byte[150];
        var payload2 = new byte[350];

        using (var s1 = new MemoryStream(payload1))
            await cache.SaveAudioAsync("S1", "en-US-JennyNeural", 1.0, s1, CancellationToken.None);

        using (var s2 = new MemoryStream(payload2))
            await cache.SaveAudioAsync("S2", "en-US-JennyNeural", 1.0, s2, CancellationToken.None);

        cache.GetTotalCacheSizeBytes().Should().Be(500);
    }

    [Fact]
    public async Task ClearCache_RemovesAllFilesFromCacheDirectory()
    {
        var cache = new AudioCacheManager(_testDir);

        using (var s1 = new MemoryStream(new byte[100]))
            await cache.SaveAudioAsync("S1", "en-US-JennyNeural", 1.0, s1, CancellationToken.None);
        using (var s2 = new MemoryStream(new byte[200]))
            await cache.SaveAudioAsync("S2", "en-US-JennyNeural", 1.0, s2, CancellationToken.None);

        cache.GetTotalCacheSizeBytes().Should().Be(300);

        cache.ClearCache();

        cache.GetTotalCacheSizeBytes().Should().Be(0);
        Directory.GetFiles(_testDir).Should().BeEmpty();
    }

    [Fact]
    public async Task PruneToLimit_DoesNothingWhenBelowLimit()
    {
        var cache = new AudioCacheManager(_testDir);

        using (var s1 = new MemoryStream(new byte[100]))
            await cache.SaveAudioAsync("S1", "en-US-JennyNeural", 1.0, s1, CancellationToken.None);

        cache.PruneToLimit(500);

        cache.GetTotalCacheSizeBytes().Should().Be(100);
    }
}
