namespace PteFloatingSentence.Windows.Infrastructure;

public sealed record AudioPlaybackState(bool IsLoading, bool IsPlaying, string? Error);

public interface ISentenceAudioPlayback : IDisposable
{
    AudioPlaybackState State { get; }
    event Action<AudioPlaybackState>? StateChanged;
    Task ToggleAsync(string text, string voice, double speed);
    void Stop();
}

public sealed class SentenceAudioPlayback : ISentenceAudioPlayback
{
    private readonly IAudioPlayer _player;
    private readonly ITtsService _tts;
    private readonly IAudioCacheManager _cache;
    private readonly object _gate = new();
    private CancellationTokenSource? _request;
    private long _generation;
    private bool _disposed;
    private AudioPlaybackState _state = new(false, false, null);

    public SentenceAudioPlayback(IAudioPlayer player, ITtsService tts, IAudioCacheManager cache)
    {
        _player = player ?? throw new ArgumentNullException(nameof(player));
        _tts = tts ?? throw new ArgumentNullException(nameof(tts));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
    }

    public AudioPlaybackState State
    {
        get { lock (_gate) return _state; }
    }

    public event Action<AudioPlaybackState>? StateChanged;

    public async Task ToggleAsync(string text, string voice, double speed)
    {
        var resolvedVoice = EdgeNeuralTtsService.ResolveVoice(voice);
        CancellationTokenSource request;
        long generation;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_state.IsLoading || _state.IsPlaying || _player.IsPlaying)
            {
                StopCore();
                return;
            }
            if (string.IsNullOrWhiteSpace(text)) return;
            request = new CancellationTokenSource();
            _request = request;
            generation = ++_generation;
            SetState(new AudioPlaybackState(true, false, null));
        }

        try
        {
            string path;
            if (!_cache.TryGetCachedAudio(text, resolvedVoice, speed, out path))
            {
                using var audio = await _tts.SynthesizeSpeechAsync(text, resolvedVoice, speed, request.Token);
                request.Token.ThrowIfCancellationRequested();
                await _cache.SaveAudioAsync(text, resolvedVoice, speed, audio, request.Token);
                request.Token.ThrowIfCancellationRequested();
                path = _cache.GetCacheFilePath(text, resolvedVoice, speed);
            }

            Task playback;
            lock (_gate)
            {
                if (!IsCurrent(generation, request)) return;
                SetState(new AudioPlaybackState(false, true, null));
                playback = _player.PlayFileAsync(path,
                    () => Finish(generation, request, null),
                    error => Finish(generation, request, $"Playback error: {error.Message}"));
            }
            await playback;
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested)
        {
            Finish(generation, request, null);
        }
        catch (Exception error)
        {
            Finish(generation, request, $"Audio error: {error.Message}");
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            if (_disposed) return;
            StopCore();
        }
    }

    private void StopCore()
    {
        ++_generation;
        var request = _request;
        _request = null;
        request?.Cancel();
        request?.Dispose();
        if (_player.IsPlaying) _player.Stop();
        SetState(new AudioPlaybackState(false, false, null));
    }

    private bool IsCurrent(long generation, CancellationTokenSource request) =>
        !_disposed && generation == _generation && ReferenceEquals(request, _request) && !request.IsCancellationRequested;

    private void Finish(long generation, CancellationTokenSource request, string? error)
    {
        lock (_gate)
        {
            if (!IsCurrent(generation, request)) return;
            ++_generation;
            _request = null;
            request.Dispose();
            if (_player.IsPlaying) _player.Stop();
            SetState(new AudioPlaybackState(false, false, error));
        }
    }

    private void SetState(AudioPlaybackState state)
    {
        if (_state == state) return;
        _state = state;
        StateChanged?.Invoke(state);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            StopCore();
            _disposed = true;
            StateChanged = null;
            _player.Dispose();
        }
    }
}
