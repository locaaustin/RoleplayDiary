namespace RoleplayDiary.Domain;

/// <summary>A character as the sidebar needs them, with their entry count and cover already resolved.</summary>
public sealed class RosterItem
{
    public RosterItem(Character character, int entryCount, string? portraitPath)
    {
        Character = character;
        EntryCount = entryCount;
        PortraitPath = portraitPath;
    }

    public Character Character { get; }

    public int EntryCount { get; }

    public string? PortraitPath { get; }

    public long Id => Character.Id;

    public string Name => Character.Name;

    public CharacterStatus Status => Character.Status;

    public string Meta => Character.MetaLine(EntryCount);
}
