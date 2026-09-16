using PteFloatingSentence.Core;

namespace PteFloatingSentence.Windows.Infrastructure;

public sealed class SettingsPersistenceQueue
{
    private readonly object _gate = new();
    private readonly Func<AppSettings, Task> _save;
    private AppSettings? _pending;
    private Task? _processing;

    public SettingsPersistenceQueue(Func<AppSettings, Task> save)
    {
        _save = save;
    }

    public void Queue(AppSettings settings)
    {
        lock (_gate)
        {
            _pending = settings;
            _processing ??= Task.Run(ProcessAsync);
        }
    }

    public async Task FlushAsync()
    {
        while (true)
        {
            Task? processing;
            lock (_gate)
            {
                processing = _processing;
            }

            if (processing is null)
                return;

            await processing;
        }
    }

    private async Task ProcessAsync()
    {
        while (true)
        {
            AppSettings? settings;
            lock (_gate)
            {
                settings = _pending;
                _pending = null;
                if (settings is null)
                {
                    _processing = null;
                    return;
                }
            }

            try
            {
                await _save(settings);
            }
            catch (Exception)
            {
                // A later save request retries the current application state.
            }
        }
    }
}
