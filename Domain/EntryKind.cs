namespace RoleplayDiary.Domain;

/// <summary>What kind of thing happened in one story entry. The integer is what gets stored.</summary>
public enum EntryKind
{
    Journal = 0,
    Milestone = 1,
    Relationship = 2,
    Career = 3,
    Crime = 4,
    Injury = 5,
    Death = 6
}
