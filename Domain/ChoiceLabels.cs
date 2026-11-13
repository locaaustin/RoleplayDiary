namespace RoleplayDiary.Domain;

/// <summary>
/// Screen wording for the stored enums. Rewording a label does not change saved rows.
/// </summary>
public static class ChoiceLabels
{
    public static string Label(this CharacterStatus status) => status switch
    {
        CharacterStatus.Alive => "Alive",
        CharacterStatus.Dead => "Deceased",
        CharacterStatus.Shelved => "Shelved",
        _ => status.ToString()
    };

    public static string Label(this EntryKind kind) => kind switch
    {
        EntryKind.Journal => "Journal",
        EntryKind.Milestone => "Milestone",
        EntryKind.Relationship => "Relationship",
        EntryKind.Career => "Career",
        EntryKind.Crime => "Crime",
        EntryKind.Injury => "Injury",
        EntryKind.Death => "Death",
        _ => kind.ToString()
    };

    /// <summary>Unknown database values fall back to the enum default instead of throwing.</summary>
    public static TEnum Coerce<TEnum>(long value) where TEnum : struct, Enum
    {
        var candidate = (TEnum)Enum.ToObject(typeof(TEnum), value);
        return Enum.IsDefined(candidate) ? candidate : default;
    }
}
