using System.Collections.ObjectModel;
using RoleplayDiary.Domain;
using RoleplayDiary.Services;

namespace RoleplayDiary.ViewModels;

/// <summary>The shell: roster on the left, the open character on the right.</summary>
public sealed class MainViewModel : ObservableObject
{
    private readonly Diary _diary;
    private readonly IDialogService _dialogs;
    private readonly List<RosterItem> _everyone = new();

    /// <summary>
    /// True while the visible list is being rebuilt. Clearing it makes the list box
    /// push a null selection, which would close the open character; this ignores that.
    /// </summary>
    private bool _rebuilding;

    private string _searchText = string.Empty;
    private StatusFilter _selectedFilter = StatusFilter.Everyone;
    private RosterItem? _selectedCharacter;
    private CharacterDetailViewModel? _detail;

    public MainViewModel(Diary diary, IDialogService dialogs)
    {
        _diary = diary;
        _dialogs = dialogs;
        NewCharacterCommand = new RelayCommand(NewCharacter);
        Refresh();
    }

    public ObservableCollection<RosterItem> VisibleCharacters { get; } = new();

    public IReadOnlyList<StatusFilter> StatusFilters => StatusFilter.All;

    public RelayCommand NewCharacterCommand { get; }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (Set(ref _searchText, value))
            {
                ApplyFilter();
            }
        }
    }

    public StatusFilter SelectedFilter
    {
        get => _selectedFilter;
        set
        {
            if (value is not null && Set(ref _selectedFilter, value))
            {
                ApplyFilter();
            }
        }
    }

    public RosterItem? SelectedCharacter
    {
        get => _selectedCharacter;
        set
        {
            if (!Set(ref _selectedCharacter, value) || _rebuilding)
            {
                return;
            }

            OpenDetail(value);
        }
    }

    public CharacterDetailViewModel? Detail
    {
        get => _detail;
        private set => Set(ref _detail, value);
    }

    public string RosterCountLabel =>
        _everyone.Count == 1 ? "1 character" : $"{_everyone.Count} characters";

    public string PlaceholderTitle =>
        _everyone.Count == 0 ? "Your diary is empty"
        : VisibleCharacters.Count == 0 ? "Nothing matches"
        : "No character open";

    public string PlaceholderBody =>
        _everyone.Count == 0
            ? "Start with a name. Age, server, background and photos can all come later, "
              + "as the character gets played."
        : VisibleCharacters.Count == 0
            ? "No character fits that search and filter. Clear them to see everyone again."
            : "Pick someone from the list to read their story.";

    public void Refresh(long? selectId = null)
    {
        var keepId = selectId ?? SelectedCharacter?.Id;

        _everyone.Clear();
        _everyone.AddRange(_diary.Roster());
        ApplyFilter(keepId);
        Raise(nameof(RosterCountLabel));
    }

    private void ApplyFilter(long? keepId = null)
    {
        keepId ??= SelectedCharacter?.Id;

        _rebuilding = true;
        VisibleCharacters.Clear();

        foreach (var item in _everyone.Where(MatchesRoster))
        {
            VisibleCharacters.Add(item);
        }

        SelectedCharacter = VisibleCharacters.FirstOrDefault(item => item.Id == keepId);
        _rebuilding = false;
        OpenDetail(SelectedCharacter);

        Raise(nameof(PlaceholderTitle), nameof(PlaceholderBody));
    }

    private bool MatchesRoster(RosterItem item) =>
        (SelectedFilter.Value is null || item.Status == SelectedFilter.Value)
        && item.Character.Matches(SearchText);

    private void OpenDetail(RosterItem? item)
    {
        if (item is null)
        {
            Detail = null;
            return;
        }

        Detail = new CharacterDetailViewModel(
            item.Character,
            _diary,
            _dialogs,
            onChanged: () => Refresh(item.Id),
            onDeleted: () =>
            {
                SelectedCharacter = null;
                Detail = null;
                Refresh(selectId: -1);
            });
    }

    private void NewCharacter()
    {
        var draft = new CharacterDraft();
        if (!_dialogs.ShowCharacterEditor(draft, portraitPath: null, isNew: true))
        {
            return;
        }

        var character = _diary.CreateCharacter(draft);

        _searchText = string.Empty;
        _selectedFilter = StatusFilter.Everyone;
        Raise(nameof(SearchText), nameof(SelectedFilter));
        Refresh(character.Id);
    }
}
