using System.Globalization;

namespace RoleplayDiary;

/// <summary>
/// Dates people actually type. Anything unrecognised means undated rather than an
/// error, because being made to format a date correctly is no way to write a diary.
/// </summary>
public static class FlexibleDate
{
    private static readonly string[] Formats =
    {
        "yyyy-MM-dd", "yyyy/MM/dd", "d/M/yyyy", "M/d/yyyy", "d-M-yyyy", "d.M.yyyy",
        "d MMMM yyyy", "d MMM yyyy", "MMMM d yyyy", "MMM d yyyy",
        "MMMM d, yyyy", "MMM d, yyyy", "MMMM yyyy", "MMM yyyy", "yyyy"
    };

    private static readonly Dictionary<string, int> Keywords =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["today"] = 0,
            ["now"] = 0,
            ["yesterday"] = -1,
            ["tomorrow"] = 1
        };

    public static DateOnly? Parse(string? text)
    {
        var cleaned = Collapse(text);
        if (cleaned.Length == 0)
        {
            return null;
        }

        if (Keywords.TryGetValue(cleaned, out var offset))
        {
            return DateOnly.FromDateTime(DateTime.Today.AddDays(offset));
        }

        return DateTime.TryParseExact(
            cleaned, Formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? DateOnly.FromDateTime(parsed)
            : null;
    }

    /// <summary>A sentence for the hint under the field, and whether it was understood.</summary>
    public static (string Message, bool Understood) Describe(string? text)
    {
        if (Collapse(text).Length == 0)
        {
            return ("No date - the entry will sit where you added it.", true);
        }

        return Parse(text) is { } parsed
            ? ($"Reads as {parsed:d MMMM yyyy}.", true)
            : ("Not a date we recognise, so this will be saved undated.", false);
    }

    private static string Collapse(string? text) =>
        string.Join(' ', (text ?? string.Empty)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
}

/// <summary>
/// The values a dialog collected, before they become a saved record.
///
/// Keeping this separate from <see cref="Character"/> is what lets the editor be
/// cancelled with nothing changed: the draft is thrown away and the real character
/// was never touched.
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
    /// Photos picked in the editor are <i>staged</i> here, not imported: a brand new
    /// character has no id to file them under yet, and cancelling must leave nothing
    /// behind. The diary imports them once the save succeeds.
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
        character.Nickname = Blank(Nickname);
        character.Age = int.TryParse(AgeText, out var age) && age is > 0 and < 1000 ? age : null;
        character.Status = Status;
        character.Server = Blank(Server);
        character.Occupation = Blank(Occupation);
        character.Affiliation = Blank(Affiliation);
        character.Background = Blank(Background);
    }

    internal static string? Blank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>The same idea for one entry in the story.</summary>
public sealed class EntryDraft
{
    public string Title { get; set; } = string.Empty;
    public EntryKind Kind { get; set; } = EntryKind.Journal;
    public string? WhenText { get; set; }
    public string? Body { get; set; }

    public bool IsValid => !string.IsNullOrWhiteSpace(Title);

    public static EntryDraft From(DiaryEntry entry) => new()
    {
        Title = entry.Title,
        Kind = entry.Kind,
        WhenText = entry.OccurredOn?.ToString("yyyy-MM-dd"),
        Body = entry.Body
    };

    public void ApplyTo(DiaryEntry entry)
    {
        entry.Title = Title.Trim();
        entry.Kind = Kind;
        entry.OccurredOn = FlexibleDate.Parse(WhenText);
        entry.Body = CharacterDraft.Blank(Body);
    }
}
