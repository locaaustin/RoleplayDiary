using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace RoleplayDiary.Views;

/// <summary>Asks Windows to draw the title bar in the same dark colour as the app.</summary>
public static class WindowChrome
{
    private const int UseImmersiveDarkMode = 20;

    public static void DarkenWhenReady(Window window) =>
        window.SourceInitialized += (_, _) => Darken(window);

    public static void Darken(Window window)
    {
        try
        {
            var handle = new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero)
            {
                return;
            }

            var enabled = 1;
            DwmSetWindowAttribute(handle, UseImmersiveDarkMode, ref enabled, sizeof(int));
        }
        catch (Exception)
        {
            // Older Windows builds keep the light title bar. The app still runs.
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        IntPtr window, int attribute, ref int value, int size);
}
