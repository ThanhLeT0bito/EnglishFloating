using System;
using System.Threading.Tasks;

namespace PteFloatingSentence.Windows.Infrastructure;

public interface IAudioPlayer : IDisposable
{
    Task PlayFileAsync(string filePath, Action onEnded, Action<Exception> onError);

    void Stop();

    bool IsPlaying { get; }
}
