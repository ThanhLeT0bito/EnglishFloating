using System.IO;
using System.Text;
using PteFloatingSentence.Windows.Infrastructure;
using Xunit;
using Assert = Xunit.Assert;

namespace PteFloatingSentence.Windows.Tests;

public class SentenceAudioPlaybackTests
{
    private const string Text = "A short sentence.";
    private const string Voice = "en-US-JennyNeural";

    [Fact]
    public async Task CacheHit_PlaysWithoutSynthesis()
    {
        var player = new FakePlayer();
        var tts = new FakeTts();
        var cache = new FakeCache { HasAudio = true };
        using var workflow = new SentenceAudioPlayback(player, tts, cache);

        await workflow.ToggleAsync(Text, Voice, 1.0);

        Assert.Equal(0, tts.Calls);
        Assert.Equal(1, player.PlayCount);
        Assert.Equal("cached.mp3", player.LastPath);
        Assert.Equal(new AudioPlaybackState(false, true, null), workflow.State);
    }

    [Fact]
    public async Task CacheMiss_SavesBeforePlaying()
    {
        var player = new FakePlayer();
        var tts = new FakeTts();
        var cache = new FakeCache();
        using var workflow = new SentenceAudioPlayback(player, tts, cache);

        await workflow.ToggleAsync(Text, Voice, 1.0);

        Assert.Equal(1, tts.Calls);
        Assert.Equal(1, cache.SaveCount);
        Assert.Equal(1, player.PlayCount);
        Assert.False(workflow.State.IsLoading);
    }

    [Fact]
    public async Task SecondToggle_StopsPlayback()
    {
        var player = new FakePlayer();
        using var workflow = new SentenceAudioPlayback(player, new FakeTts(), new FakeCache { HasAudio = true });
        await workflow.ToggleAsync(Text, Voice, 1.0);

        await workflow.ToggleAsync(Text, Voice, 1.0);

        Assert.Equal(1, player.StopCount);
        Assert.Equal(new AudioPlaybackState(false, false, null), workflow.State);
    }

    [Fact]
    public async Task Stop_CancelsPendingSynthesisAndRejectsLateResult()
    {
        var player = new FakePlayer();
        var tts = new FakeTts { Pending = true };
        using var workflow = new SentenceAudioPlayback(player, tts, new FakeCache());
        var request = workflow.ToggleAsync(Text, Voice, 1.0);
        Assert.True(workflow.State.IsLoading);

        workflow.Stop();
        Assert.True(tts.LastToken.IsCancellationRequested);
        tts.Complete();
        await request;

        Assert.Equal(0, player.PlayCount);
        Assert.Equal(new AudioPlaybackState(false, false, null), workflow.State);
    }

    [Fact]
    public async Task Timeout_LeavesRetryableErrorAndNextToggleRetries()
    {
        var player = new FakePlayer();
        var tts = new FakeTts { Failure = new TimeoutException("Timed out") };
        using var workflow = new SentenceAudioPlayback(player, tts, new FakeCache());

        await workflow.ToggleAsync(Text, Voice, 1.0);
        Assert.False(workflow.State.IsLoading);
        Assert.False(workflow.State.IsPlaying);
        Assert.Contains("Timed out", workflow.State.Error);

        tts.Failure = null;
        await workflow.ToggleAsync(Text, Voice, 1.0);
        Assert.Equal(2, tts.Calls);
        Assert.True(workflow.State.IsPlaying);
        Assert.Null(workflow.State.Error);
    }

    [Fact]
    public async Task StalePlaybackCallback_CannotChangeNewPlayback()
    {
        var player = new FakePlayer();
        using var workflow = new SentenceAudioPlayback(player, new FakeTts(), new FakeCache { HasAudio = true });
        await workflow.ToggleAsync(Text, Voice, 1.0);
        var staleEnded = player.OnEnded!;
        workflow.Stop();
        await workflow.ToggleAsync(Text, Voice, 1.0);

        staleEnded();

        Assert.True(workflow.State.IsPlaying);
        Assert.Equal(2, player.PlayCount);
    }

    [Fact]
    public async Task Dispose_StopsAndDisposesPlayerAndIgnoresLateCallback()
    {
        var player = new FakePlayer();
        var workflow = new SentenceAudioPlayback(player, new FakeTts(), new FakeCache { HasAudio = true });
        await workflow.ToggleAsync(Text, Voice, 1.0);
        var ended = player.OnEnded!;

        workflow.Dispose();
        ended();

        Assert.True(player.StopCount > 0);
        Assert.Equal(1, player.DisposeCount);
        Assert.Equal(new AudioPlaybackState(false, false, null), workflow.State);
    }

    private sealed class FakePlayer : IAudioPlayer
    {
        public bool IsPlaying { get; private set; }
        public int PlayCount { get; private set; }
        public int StopCount { get; private set; }
        public int DisposeCount { get; private set; }
        public string? LastPath { get; private set; }
        public Action? OnEnded { get; private set; }

        public Task PlayFileAsync(string filePath, Action onEnded, Action<Exception> onError)
        {
            LastPath = filePath;
            OnEnded = onEnded;
            PlayCount++;
            IsPlaying = true;
            return Task.CompletedTask;
        }

        public void Stop() { StopCount++; IsPlaying = false; }
        public void Dispose() { DisposeCount++; IsPlaying = false; }
    }

    private sealed class FakeTts : ITtsService
    {
        private TaskCompletionSource<Stream>? _completion;
        public bool Pending { get; set; }
        public Exception? Failure { get; set; }
        public int Calls { get; private set; }
        public CancellationToken LastToken { get; private set; }

        public Task<Stream> SynthesizeSpeechAsync(string text, string voice, double speed, CancellationToken cancellationToken = default)
        {
            Calls++;
            LastToken = cancellationToken;
            if (Failure is not null) return Task.FromException<Stream>(Failure);
            if (Pending)
            {
                _completion = new TaskCompletionSource<Stream>(TaskCreationOptions.RunContinuationsAsynchronously);
                return _completion.Task;
            }
            return Task.FromResult<Stream>(new MemoryStream(Encoding.UTF8.GetBytes("audio")));
        }

        public void Complete() => _completion!.SetResult(new MemoryStream(Encoding.UTF8.GetBytes("late audio")));
    }

    private sealed class FakeCache : IAudioCacheManager
    {
        public bool HasAudio { get; set; }
        public int SaveCount { get; private set; }
        public string GetCacheFilePath(string text, string voice, double speed) => "cached.mp3";
        public bool TryGetCachedAudio(string text, string voice, double speed, out string filePath)
        {
            filePath = HasAudio ? "cached.mp3" : string.Empty;
            return HasAudio;
        }
        public Task SaveAudioAsync(string text, string voice, double speed, Stream audioStream, CancellationToken cancellationToken = default)
        {
            SaveCount++;
            HasAudio = true;
            return Task.CompletedTask;
        }
        public long GetTotalCacheSizeBytes() => 0;
        public void ClearCache() => HasAudio = false;
        public void PruneToLimit(long maxSizeBytes) { }
    }
}
