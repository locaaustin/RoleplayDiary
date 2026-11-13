using System.Windows;

namespace RoleplayDiary.Views;

/// <summary>Confirm and inform dialog, drawn in the app theme instead of the system message box.</summary>
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
