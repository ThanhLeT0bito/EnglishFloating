using System.Configuration;
using System.Data;
using System.Windows;

namespace PteFloatingSentence.Windows;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : System.Windows.Application
{
    private readonly Infrastructure.JsonSettingsStore _settingsStore = new();
    private readonly Infrastructure.SettingsPersistenceQueue _persistenceQueue;
    private Core.AppSettings _settings = Core.AppSettings.Default;
    private FloatingWindow? _floatingWindow;
    private SettingsWindow? _settingsWindow;
    private bool _isShuttingDown;

    public App()
    {
        _persistenceQueue = new(_settingsStore.SaveAsync);
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _settings = await _settingsStore.LoadAsync();
        var displays = Core.DisplayProjection.PrimaryFirst(System.Windows.Forms.Screen.AllScreens
            .Select(screen => (
                new Core.DisplayBounds(
                    screen.WorkingArea.Left,
                    screen.WorkingArea.Top,
                    screen.WorkingArea.Right,
                    screen.WorkingArea.Bottom),
                screen.Primary)));
        var position = Core.WindowPlacementNormalizer.Normalize(_settings, displays);
        _settings = _settings with { Left = position.Left, Top = position.Top };

        _floatingWindow = new FloatingWindow { Left = position.Left, Top = position.Top };
        _floatingWindow.ApplySettings(_settings);
        _floatingWindow.SettingsRequested += FloatingWindow_SettingsRequested;
        _floatingWindow.ExitRequested += async (_, _) => await ShutdownAsync();
        _floatingWindow.PreviousRequested += (_, _) => NavigateCurrentSentence(-1);
        _floatingWindow.NextRequested += (_, _) => NavigateCurrentSentence(1);
        _floatingWindow.PositionChanged += FloatingWindow_PositionChanged;
        _floatingWindow.Show();
    }

    private void FloatingWindow_SettingsRequested(object? sender, EventArgs e)
    {
        if (_settingsWindow is not null)
        {
            _settingsWindow.Activate();
            return;
        }

        _settingsWindow = new SettingsWindow(_settings, SaveSettings);
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
    }

    private void FloatingWindow_PositionChanged(object? sender, (double Left, double Top) position)
    {
        _settings = _settings with { Left = position.Left, Top = position.Top };
        PersistSettings();
    }

    private void NavigateCurrentSentence(int direction)
    {
        _settings = Core.StudyListRules.MoveCurrentSentence(_settings, direction);
        _floatingWindow?.ApplySettings(_settings);
        PersistSettings();
    }

    private void SaveSettings(Core.AppSettings settings)
    {
        _settings = Core.SettingsUpdateMerger.MergeEditableFields(_settings, settings);
        _floatingWindow?.ApplySettings(_settings);
        PersistSettings();
    }

    private void PersistSettings()
    {
        if (!_isShuttingDown)
            _persistenceQueue.Queue(_settings);
    }

    private async Task ShutdownAsync()
    {
        if (_isShuttingDown)
            return;

        _isShuttingDown = true;
        await _persistenceQueue.FlushAsync();
        Shutdown();
    }
}
