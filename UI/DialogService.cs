using System.Diagnostics;
using System.Windows;
using Microsoft.Win32;

namespace RoleplayDiary;

/// <summary>
/// Everything that opens a window of its own, behind one interface.
///
/// The view models depend on this rather than on WPF directly. That is what keeps
/// them free of windows: a view model asks "pick me some images" and does not care
/// that a file dialog is how it happens.
/// </summary>
public interface IDialogService
{
    IReadOnlyList<string> PickImages();

    string? PickPdfDestination(string startIn, string suggestedName);

    bool Confirm(string title, string body, string confirmLabel = "Delete");

    bool Offer(string title, string body, string yesLabel, string noLabel = "Close");

    void Inform(string title, string body);

    bool ShowCharacterEditor(CharacterDraft draft, string? portraitPath, bool isNew);

    bool ShowEntryEditor(EntryDraft draft, bool isNew);

    void OpenInShell(string path);
}

/// <summary>The real implementation, which does know about WPF.</summary>
public sealed class DialogService : IDialogService
{
    private string _lastImageFolder =
        Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);

    /// <summary>Set once the main window exists, so dialogs centre on it.</summary>
    public Window? Owner { get; set; }

    public IReadOnlyList<string> PickImages()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Add photos",
            Multiselect = true,
            InitialDirectory = _lastImageFolder,
            Filter = PhotoStorage.FileDialogFilter()
        };

        if (dialog.ShowDialog(Owner) != true)
        {
            return Array.Empty<string>();
        }

        // Players add screenshots in batches from the same folder, so remember it.
        var folder = Path.GetDirectoryName(dialog.FileNames[0]);
        if (!string.IsNullOrEmpty(folder))
        {
            _lastImageFolder = folder;
        }

        return dialog.FileNames;
    }

    public string? PickPdfDestination(string startIn, string suggestedName)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Export character as PDF",
            InitialDirectory = startIn,
            FileName = suggestedName,
            DefaultExt = ".pdf",
            Filter = "PDF document (*.pdf)|*.pdf"
        };

        if (dialog.ShowDialog(Owner) != true)
        {
            return null;
        }

        return dialog.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)
            ? dialog.FileName
            : dialog.FileName + ".pdf";
    }

    public bool Confirm(string title, string body, string confirmLabel = "Delete") =>
        MessageWindow.Ask(Owner, title, body, confirmLabel, "Cancel", destructive: true);

    public bool Offer(string title, string body, string yesLabel, string noLabel = "Close") =>
        MessageWindow.Ask(Owner, title, body, yesLabel, noLabel, destructive: false);

    public void Inform(string title, string body) =>
        MessageWindow.Ask(Owner, title, body, confirmLabel: null, "Close", destructive: false);

    public bool ShowCharacterEditor(CharacterDraft draft, string? portraitPath, bool isNew)
    {
        var window = new CharacterWindow(draft, portraitPath, isNew) { Owner = Owner };
        return window.ShowDialog() == true;
    }

    public bool ShowEntryEditor(EntryDraft draft, bool isNew)
    {
        var window = new EntryWindow(draft, isNew) { Owner = Owner };
        return window.ShowDialog() == true;
    }

    public void OpenInShell(string path)
    {
        try
        {
            // UseShellExecute is what makes Windows open the file in whatever the
            // user has set as their PDF reader, rather than trying to run it.
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception)
        {
            // Opening the result is a courtesy; failing at it is not worth a dialog.
        }
    }
}
