namespace RoleplayDiary.Domain;

/// <summary>
/// Values collected by the character editor before they become a saved
/// <see cref="Character"/>. Cancelling the editor discards the draft.
/// </summary>
public sealed class CharacterDraft
{
    public string Name { get; set; } = string.Empty;

    public string? Nickname { get; set; }

    public string? AgeText { get; set; }

    public CharacterStatus Status { get; set; } = CharacterStatus.Alive;

    public string? Server { get; set; }

    public string? Occupation { get; set; }

    public string? Affiliation { get; set; }

    public string? Background { get; set; }

    /// <summary>
    /// Photos picked before save. They are imported only after the character has an id,
    /// and cancelling the editor leaves them unimported.
    /// </summary>
    public List<string> Photos { get; } = new();

    public bool IsValid => !string.IsNullOrWhiteSpace(Name);

    public static CharacterDraft From(Character character) => new()
    {
        Name = character.Name,
        Nickname = character.Nickname,
        AgeText = character.Age?.ToString(),
        Status = character.Status,
        Server = character.Server,
        Occupation = character.Occupation,
        Affiliation = character.Affiliation,
        Background = character.Background
    };

    public void ApplyTo(Character character)
    {
        character.Name = Name.Trim();
        character.Nickname = StoredText.OrNull(Nickname);
        character.Age = int.TryParse(AgeText, out var age) && age is > 0 and < 1000 ? age : null;
        character.Status = Status;
        character.Server = StoredText.OrNull(Server);
        character.Occupation = StoredText.OrNull(Occupation);
        character.Affiliation = StoredText.OrNull(Affiliation);
        character.Background = StoredText.OrNull(Background);
    }
}
