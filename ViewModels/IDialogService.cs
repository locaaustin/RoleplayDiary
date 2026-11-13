using RoleplayDiary.Domain;

namespace RoleplayDiary.ViewModels;

/// <summary>
/// Windows and file dialogs, as the view models are allowed to see them.
/// The implementation lives with the views.
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
