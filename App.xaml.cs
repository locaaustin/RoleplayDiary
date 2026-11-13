using System.Windows;
using System.Windows.Threading;
using RoleplayDiary.Services;
using RoleplayDiary.ViewModels;
using RoleplayDiary.Views;

namespace RoleplayDiary;

/// <summary>
/// Builds the diary, the dialogs, and the main window once, then hands each
/// screen what it needs. Nothing else reaches for a global.
/// </summary>
public partial class App : Application
{
    private Diary? _diary;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += OnUnhandledException;

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
        }

        MessageWindow.Ask(
            MainWindow,
            "Roleplay Diary",
            $"Something went wrong.\n\n{e.Exception.GetType().Name}: {e.Exception.Message}\n\n"
            + $"The details were written to:\n{logPath}\n\nYour diary itself is unaffected.",
            confirmLabel: null,
            dismissLabel: "Close",
            destructive: false);

        e.Handled = true;
    }
}
