namespace RoleplayDiary.Domain;

/// <summary>Whether a character is still being played. The integer is what gets stored.</summary>
public enum CharacterStatus
{
    Alive = 0,
    Dead = 1,
    Shelved = 2
}
