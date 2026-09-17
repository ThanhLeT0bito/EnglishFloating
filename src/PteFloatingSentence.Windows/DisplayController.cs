using System.Windows;
using PteFloatingSentence.Core;

namespace PteFloatingSentence.Windows;

public sealed class DisplayController : IDisposable
{
    private readonly FloatingWindow _floatingWindow;
    private readonly OverlayLauncherWindow _launcherWindow;
    private bool _disposed;

    public event EventHandler? ShowSettingsRequested;
    public event EventHandler? RestoreOverlayRequested;

    public DisplayController(FloatingWindow floatingWindow, OverlayLauncherWindow? launcherWindow = null)
    {
        _floatingWindow = floatingWindow;
        _launcherWindow = launcherWindow ?? new OverlayLauncherWindow();

        _launcherWindow.SettingsRequested += LauncherWindow_SettingsRequested;
        _launcherWindow.RestoreRequested += LauncherWindow_RestoreRequested;
    }

    public FloatingWindow FloatingWindow => _floatingWindow;
    public OverlayLauncherWindow LauncherWindow => _launcherWindow;

    public void Apply(AppSettings settings)
    {
        _floatingWindow.ApplySettings(settings, renderContent: settings.ShowSentenceOverlay);

        if (settings.ShowSentenceOverlay)
        {
            _floatingWindow.Show();
            _launcherWindow.Hide();
        }
        else
        {
            _floatingWindow.Hide();
            _launcherWindow.Show();
        }
    }

    private void LauncherWindow_SettingsRequested(object? sender, EventArgs e) =>
        ShowSettingsRequested?.Invoke(this, EventArgs.Empty);

    private void LauncherWindow_RestoreRequested(object? sender, EventArgs e) =>
        RestoreOverlayRequested?.Invoke(this, EventArgs.Empty);

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _launcherWindow.SettingsRequested -= LauncherWindow_SettingsRequested;
        _launcherWindow.RestoreRequested -= LauncherWindow_RestoreRequested;
        _launcherWindow.Close();
    }
}
