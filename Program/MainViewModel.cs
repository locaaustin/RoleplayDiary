using System.Collections.ObjectModel;

namespace RoleplayDiary;

/// <summary>One of the chips above the roster.</summary>
public sealed class StatusFilter
{
    public StatusFilter(string label, CharacterStatus? value)
    {
        Label = label;
        Value = value;
    }

    public string Label { get; }

    /// <summary>Null means "All".</summary>
    public CharacterStatus? Value { get; }

    public static IReadOnlyList<StatusFilter> All { get; } = new[]
    {
        new StatusFilter("All", null),
        new StatusFilter("Alive", CharacterStatus.Alive),
        new StatusFilter("Deceased", CharacterStatus.Dead),
        new StatusFilter("Shelved", CharacterStatus.Shelved)
    };
}

/// <summary>
/// The shell: the roster on the left, the open character on the right.
///
/// It holds the running state of the app - what is searched for, which filter is on,
/// who is selected - and rebuilds what is visible whenever any of that changes.
/// </summary>
public sealed class MainViewModel : ObservableObject
{
    private readonly Diary _diary;
    private readonly IDialogService _dialogs;

    private List<RosterItem> _everyone = new();

    /// <summary>
    /// True while the visible list is being rebuilt. Clearing the collection makes
    /// the list box push a null selection back at us, which would tear down the open
    /// character mid-edit; this flag lets that transient null pass through untouched.
    /// </summary>
    private bool _rebuilding;

    public MainViewModel(Diary diary, IDialogService dialogs)
    {
        _diary = diary;
        _dialogs = dialogs;

        NewCharacterCommand = new RelayCommand(NewCharacter);
        ClearSearchCommand = new RelayCommand(() => SearchText = string.Empty);

        Refresh();
    }

    public ObservableCollection<RosterItem> VisibleCharacters { get; } = new();

    public IReadOnlyList<StatusFilter> StatusFilters => StatusFilter.All;

    public RelayCommand NewCharacterCommand { get; }

    public RelayCommand ClearSearchCommand { get; }

    // ------------------------------------------------------------------ state

    private string _searchText = string.Empty;

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

    private StatusFilter _selectedFilter = StatusFilter.All[0];

    public StatusFilter SelectedFilter
    {
        get => _selectedFilter;
        set
        {
            // A list box can briefly report no selection while it is rebuilt.
            if (value is not null && Set(ref _selectedFilter, value))
            {
                ApplyFilter();
            }
        }
    }

    private RosterItem? _selectedCharacter;

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

    private CharacterDetailViewModel? _detail;

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

    // --------------------------------------------------------------- rebuild

    /// <summary>Re-read everything from the diary, then restore what was open.</summary>
    public void Refresh(long? selectId = null)
    {
        var keepId = selectId ?? SelectedCharacter?.Id;

        _everyone = _diary.Roster();
        ApplyFilter(keepId);

        Raise(nameof(RosterCountLabel));
    }

    private void ApplyFilter(long? keepId = null)
    {
        keepId ??= SelectedCharacter?.Id;

        var matches = _everyone.Where(item =>
            (SelectedFilter.Value is null || item.Status == SelectedFilter.Value) &&
            item.Character.Matches(SearchText));

        _rebuilding = true;

        VisibleCharacters.Clear();
        foreach (var item in matches)
        {
            VisibleCharacters.Add(item);
        }

        // Re-select the same character so the open profile survives the rebuild.
        SelectedCharacter = VisibleCharacters.FirstOrDefault(item => item.Id == keepId);

        _rebuilding = false;

        // Either reattach the detail pane to the freshly loaded character, or close it.
        OpenDetail(SelectedCharacter);

        Raise(nameof(PlaceholderTitle), nameof(PlaceholderBody));
    }

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

    // --------------------------------------------------------------- commands

    private void NewCharacter()
    {
        var draft = new CharacterDraft();
        if (!_dialogs.ShowCharacterEditor(draft, portraitPath: null, isNew: true))
        {
            return;
        }

        var character = _diary.CreateCharacter(draft);

        // Clear the filters, so the character they just made is certainly on screen.
        _searchText = string.Empty;
        _selectedFilter = StatusFilter.All[0];
        Raise(nameof(SearchText), nameof(SelectedFilter));

        Refresh(character.Id);
    }
}
