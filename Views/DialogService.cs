using System.Diagnostics;
using System.Windows;
using Microsoft.Win32;
using RoleplayDiary.Domain;
using RoleplayDiary.ViewModels;

namespace RoleplayDiary.Views;

/// <summary>Opens the app's own windows and the Windows file dialogs.</summary>
public sealed class DialogService : IDialogService
{
    private string _lastImageFolder =
        Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);

    public Window? Owner { get; set; }

    public IReadOnlyList<string> PickImages()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Add photos",
            Multiselect = true,
            InitialDirectory = _lastImageFolder,
            Filter = ImageFiles.FileDialogFilter()
        };

        if (dialog.ShowDialog(Owner) != true)
        {
            return Array.Empty<string>();
        }

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
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception)
        {
            // Opening the file is a courtesy. Failing to do so is not worth another dialog.
        }
    }
}
