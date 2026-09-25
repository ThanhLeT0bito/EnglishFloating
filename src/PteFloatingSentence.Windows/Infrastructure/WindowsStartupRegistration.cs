using System.Runtime.InteropServices;
using System.IO;

namespace PteFloatingSentence.Windows.Infrastructure;

public sealed record StartupRegistrationResult(bool IsSuccess, string? Error)
{
    public static StartupRegistrationResult Success { get; } = new(true, null);
}

public interface IStartupShortcutStore
{
    StartupRegistrationResult CreateOrUpdate(string shortcutFileName, string executablePath);

    StartupRegistrationResult Remove(string shortcutFileName);
}

public sealed class WindowsStartupRegistration
{
    public const string ShortcutFileName = "EnglishFloating.lnk";

    private readonly IStartupShortcutStore _shortcutStore;

    public WindowsStartupRegistration()
        : this(new WindowsShellStartupShortcutStore())
    {
    }

    public WindowsStartupRegistration(IStartupShortcutStore shortcutStore)
    {
        ArgumentNullException.ThrowIfNull(shortcutStore);
        _shortcutStore = shortcutStore;
    }

    public StartupRegistrationResult Reconcile(bool enabled, string executablePath)
    {
        if (!enabled)
            return _shortcutStore.Remove(ShortcutFileName);

        return IsPublishedExecutable(executablePath)
            ? _shortcutStore.CreateOrUpdate(ShortcutFileName, executablePath)
            : StartupRegistrationResult.Success;
    }

    public static bool IsPublishedExecutable(string executablePath) =>
        !string.IsNullOrWhiteSpace(executablePath)
        && Path.GetExtension(executablePath).Equals(".exe", StringComparison.OrdinalIgnoreCase)
        && File.Exists(executablePath);
}

internal sealed class WindowsShellStartupShortcutStore : IStartupShortcutStore
{
    public StartupRegistrationResult CreateOrUpdate(string shortcutFileName, string executablePath)
    {
        object? shell = null;
        object? shortcut = null;

        try
        {
            var shortcutPath = GetShortcutPath(shortcutFileName);
            Directory.CreateDirectory(Path.GetDirectoryName(shortcutPath)!);

            var shellType = Type.GetTypeFromProgID("WScript.Shell")
                ?? throw new InvalidOperationException("Windows Script Host is unavailable.");
            shell = Activator.CreateInstance(shellType)
                ?? throw new InvalidOperationException("Could not create Windows Script Host.");
            shortcut = ((dynamic)shell).CreateShortcut(shortcutPath);
            ((dynamic)shortcut).TargetPath = executablePath;
            ((dynamic)shortcut).WorkingDirectory = Path.GetDirectoryName(executablePath) ?? string.Empty;
            ((dynamic)shortcut).Description = "Start EnglishFloating when you sign in to Windows";
            ((dynamic)shortcut).Save();
            return StartupRegistrationResult.Success;
        }
        catch (Exception exception)
        {
            return new StartupRegistrationResult(false, exception.Message);
        }
        finally
        {
            ReleaseComObject(shortcut);
            ReleaseComObject(shell);
        }
    }

    public StartupRegistrationResult Remove(string shortcutFileName)
    {
        try
        {
            var shortcutPath = GetShortcutPath(shortcutFileName);
            if (File.Exists(shortcutPath))
                File.Delete(shortcutPath);

            return StartupRegistrationResult.Success;
        }
        catch (Exception exception)
        {
            return new StartupRegistrationResult(false, exception.Message);
        }
    }

    private static string GetShortcutPath(string shortcutFileName) =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), shortcutFileName);

    private static void ReleaseComObject(object? value)
    {
        if (value is not null && Marshal.IsComObject(value))
            Marshal.FinalReleaseComObject(value);
    }
}
