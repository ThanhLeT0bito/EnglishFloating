using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace PteFloatingSentence.Windows;

/// <summary>
/// Paints the native title bar of a window in the app's dark colors. Windows 11 takes the exact caption, text and
/// border colors; Windows 10 (1809 and later) switches to its dark title bar. Older systems keep the default bar.
/// The native bar stays, so snapping, the window buttons and dragging work as usual.
/// </summary>
public static class DarkTitleBar
{
    /// <summary>Slate 900, the Settings window background.</summary>
    public const uint CaptionColor = 0x002A170F;

    /// <summary>Slate 200, the app's main text.</summary>
    public const uint TextColor = 0x00F0E8E2;

    /// <summary>Slate 800, the app's panel borders.</summary>
    public const uint BorderColor = 0x003B291E;

    private const int UseImmersiveDarkMode = 20;
    private const int UseImmersiveDarkModeBefore20H1 = 19;
    private const int BorderColorAttribute = 34;
    private const int CaptionColorAttribute = 35;
    private const int TextColorAttribute = 36;

    /// <summary>Applies the dark title bar now if the window has a handle, otherwise as soon as it gets one.</summary>
    public static void Apply(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        var handle = new WindowInteropHelper(window).Handle;
        if (handle != IntPtr.Zero)
        {
            Apply(handle);
            return;
        }

        void OnSourceInitialized(object? sender, EventArgs e)
        {
            window.SourceInitialized -= OnSourceInitialized;
            Apply(new WindowInteropHelper(window).Handle);
        }

        window.SourceInitialized += OnSourceInitialized;
    }

    private static void Apply(IntPtr handle)
    {
        if (handle == IntPtr.Zero || !OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763))
            return;

        try
        {
            var on = 1;
            if (DwmSetWindowAttribute(handle, UseImmersiveDarkMode, ref on, sizeof(int)) != 0)
                DwmSetWindowAttribute(handle, UseImmersiveDarkModeBefore20H1, ref on, sizeof(int));

            // Windows 11 only; earlier versions return an error and keep the dark bar set above.
            var caption = unchecked((int)CaptionColor);
            var text = unchecked((int)TextColor);
            var border = unchecked((int)BorderColor);
            DwmSetWindowAttribute(handle, CaptionColorAttribute, ref caption, sizeof(int));
            DwmSetWindowAttribute(handle, TextColorAttribute, ref text, sizeof(int));
            DwmSetWindowAttribute(handle, BorderColorAttribute, ref border, sizeof(int));
        }
        catch (Exception error) when (error is DllNotFoundException or EntryPointNotFoundException)
        {
            // No desktop window manager: keep the default title bar.
        }
    }

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
