using RoleplayDiary.Domain;

namespace RoleplayDiary.ViewModels;

/// <summary>One chip above the roster. A null status means every character.</summary>
public sealed class StatusFilter
{
    public static StatusFilter Everyone { get; } = new("All", null);

    public static IReadOnlyList<StatusFilter> All { get; } =
    [
        Everyone,
        new StatusFilter("Alive", CharacterStatus.Alive),
        new StatusFilter("Deceased", CharacterStatus.Dead),
        new StatusFilter("Shelved", CharacterStatus.Shelved)
    ];

    public StatusFilter(string label, CharacterStatus? value)
    {
        Label = label;
        Value = value;
    }

    public string Label { get; }

    public CharacterStatus? Value { get; }
}
