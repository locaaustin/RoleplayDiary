using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace RoleplayDiary;

/// <summary>
/// Windows draws the title bar itself, and by default it draws it light. One call
/// per window makes it match the app.
/// </summary>
public static class WindowChrome
{
    private const int UseImmersiveDarkMode = 20;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        IntPtr window, int attribute, ref int value, int size);

    /// <summary>Call once the window has a handle, which means on or after SourceInitialized.</summary>
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
            // Cosmetic only - an older Windows build simply keeps the light bar.
        }
    }

    /// <summary>Wires Darken to fire at the right moment for any window.</summary>
    public static void DarkenWhenReady(Window window) =>
        window.SourceInitialized += (_, _) => Darken(window);
}

/// <summary>
/// The composition root. Everything is built here, once, and handed down: the diary,
/// the dialog service, the main view model, the window.
///
/// A new screen takes a constructor argument. Nothing reaches for a global.
/// </summary>
public partial class App : Application
{
    private Diary? _diary;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        InstallCrashHandler();

        // An optional folder argument lets the app run against a throwaway diary,
        // which is how it gets tested without touching the real one.
        var root = e.Args.Length > 0 ? e.Args[0] : null;

        _diary = new Diary(root);

        var dialogs = new DialogService();
        var window = new MainWindow
        {
            DataContext = new MainViewModel(_diary, dialogs)
        };

        dialogs.Owner = window;
        MainWindow = window;
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _diary?.Dispose();
        base.OnExit(e);
    }

    /// <summary>
    /// An unhandled error should leave a file a bug report can be built from, not
    /// just a window that vanishes.
    /// </summary>
    private void InstallCrashHandler() =>
        DispatcherUnhandledException += OnUnhandledException;

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        var logPath = _diary?.Paths.CrashLog
                      ?? Path.Combine(Path.GetTempPath(), "roleplaydiary-crash.log");

        try
        {
            File.AppendAllText(
                logPath,
                $"\n===== {DateTime.Now:yyyy-MM-dd HH:mm:ss} =====\n{e.Exception}\n");
        }
        catch (IOException)
        {
            // Nothing useful left to do if even the log cannot be written.
        }

        MessageWindow.Ask(
            MainWindow,
            "Roleplay Diary",
            $"Something went wrong.\n\n{e.Exception.GetType().Name}: {e.Exception.Message}\n\n"
            + $"The details were written to:\n{logPath}\n\nYour diary itself is unaffected.",
            confirmLabel: null,
            dismissLabel: "Close",
            destructive: false);

        // Handled, so one bad button press does not take the whole app down with it.
        e.Handled = true;
    }
}
