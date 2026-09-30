using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Media;

namespace PteFloatingSentence.Windows.Infrastructure;

public sealed class WpfAudioPlayer : IAudioPlayer
{
    private MediaPlayer? _mediaPlayer;
    private Action? _onEnded;
    private Action<Exception>? _onError;
    private bool _eventsAttached;
    private bool _isPlaying;
    private bool _disposed;

    public WpfAudioPlayer()
    {
        _mediaPlayer = new MediaPlayer();
    }

    internal WpfAudioPlayer(MediaPlayer mediaPlayer)
    {
        _mediaPlayer = mediaPlayer ?? throw new ArgumentNullException(nameof(mediaPlayer));
    }

    public bool IsPlaying => _isPlaying;

    public Task PlayFileAsync(string filePath, Action onEnded, Action<Exception> onError)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentNullException.ThrowIfNull(onEnded);
        ArgumentNullException.ThrowIfNull(onError);

        StopInternal();

        _onEnded = onEnded;
        _onError = onError;

        if (_mediaPlayer is null)
        {
            _mediaPlayer = new MediaPlayer();
        }

        AttachEvents();

        try
        {
            _isPlaying = true;
            var fullPath = Path.GetFullPath(filePath);
            _mediaPlayer.Open(new Uri(fullPath, UriKind.Absolute));
            _mediaPlayer.Play();
        }
        catch (Exception ex)
        {
            StopInternal();
            onError(ex);
        }

        return Task.CompletedTask;
    }

    public void Stop()
    {
        if (_disposed)
            return;

        StopInternal();
    }

    private void StopInternal()
    {
        DetachEvents();
        _onEnded = null;
        _onError = null;

        if (_mediaPlayer is not null)
        {
            try
            {
                _mediaPlayer.Stop();
                _mediaPlayer.Close();
            }
            catch
            {
                // Best effort cleanup
            }
        }

        _isPlaying = false;
    }

    private void AttachEvents()
    {
        if (_mediaPlayer is not null && !_eventsAttached)
        {
            _mediaPlayer.MediaEnded += MediaPlayer_MediaEnded;
            _mediaPlayer.MediaFailed += MediaPlayer_MediaFailed;
            _eventsAttached = true;
        }
    }

    private void DetachEvents()
    {
        if (_mediaPlayer is not null && _eventsAttached)
        {
            _mediaPlayer.MediaEnded -= MediaPlayer_MediaEnded;
            _mediaPlayer.MediaFailed -= MediaPlayer_MediaFailed;
            _eventsAttached = false;
        }
    }

    private void MediaPlayer_MediaEnded(object? sender, EventArgs e)
    {
        var callback = _onEnded;
        StopInternal();
        callback?.Invoke();
    }

    private void MediaPlayer_MediaFailed(object? sender, ExceptionEventArgs e)
    {
        var callback = _onError;
        var ex = e.ErrorException ?? new Exception("Media playback failed.");
        StopInternal();
        callback?.Invoke(ex);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        StopInternal();
        _mediaPlayer = null;
    }
}
