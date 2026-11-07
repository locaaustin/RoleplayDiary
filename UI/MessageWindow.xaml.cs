using System.Windows;

namespace RoleplayDiary;

/// <summary>
/// The app's own confirm/inform box.
///
/// Windows' built-in message box is a native dialog and arrives bright white in the
/// middle of a dark app, so this stands in for it everywhere.
/// </summary>
public partial class MessageWindow : Window
{
    private MessageWindow(
        string heading,
        string body,
        string? confirmLabel,
        string dismissLabel,
        bool destructive)
    {
        Heading = heading;
        Body = body;

        InitializeComponent();
        WindowChrome.DarkenWhenReady(this);

        DismissButton.Content = dismissLabel;

        if (confirmLabel is null)
        {
            // An informational box has nothing to confirm, so it gets one button.
            ConfirmButton.Visibility = Visibility.Collapsed;
            DismissButton.IsDefault = true;
        }
        else
        {
            ConfirmButton.Content = confirmLabel;
            ConfirmButton.Style = destructive
                ? (Style)FindResource("DangerButton")
                : (Style)FindResource("PrimaryButton");
        }
    }

    public string Heading { get; }

    public string Body { get; }

    /// <summary>
    /// Shows the box and waits. True means the player pressed the confirming button.
    /// </summary>
    public static bool Ask(
        Window? owner,
        string heading,
        string body,
        string? confirmLabel,
        string dismissLabel,
        bool destructive)
    {
        var window = new MessageWindow(heading, body, confirmLabel, dismissLabel, destructive);

        if (owner is not null && !ReferenceEquals(owner, window))
        {
            window.Owner = owner;
        }
        else
        {
            window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        return window.ShowDialog() == true;
    }

    private void OnConfirm(object sender, RoutedEventArgs e) => DialogResult = true;

    private void OnDismiss(object sender, RoutedEventArgs e) => DialogResult = false;
}
