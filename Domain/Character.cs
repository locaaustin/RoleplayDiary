namespace RoleplayDiary.Domain;

/// <summary>One character: identity fields, plus the slow-moving life story.</summary>
public sealed class Character : StoredEntity
{
    public string Name { get; set; } = string.Empty;

    public string? Nickname { get; set; }

    public int? Age { get; set; }

    public string? Server { get; set; }

    public string? Occupation { get; set; }

    public string? Affiliation { get; set; }

    public CharacterStatus Status { get; set; } = CharacterStatus.Alive;

    public string? Background { get; set; }

    public long? PortraitPhotoId { get; set; }

    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

    public void MarkUpdated() => UpdatedUtc = DateTime.UtcNow;

    public string QuotedNickname =>
        string.IsNullOrWhiteSpace(Nickname) ? string.Empty : $"“{Nickname}”";

    public string AgeLabel => Age is > 0 ? $"{Age} years old" : string.Empty;

    public string ArchivedLabel => $"archived {CreatedUtc.ToLocalTime():d MMMM yyyy}";

    public string Initials
    {
        get
        {
            var words = Name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var letters = string.Concat(words.Take(2).Select(word => word[0])).ToUpperInvariant();
            return letters.Length > 0 ? letters : "?";
        }
    }

    public string FactsLine =>
        string.Join(" · ",
            new[] { AgeLabel, Server, Occupation, Affiliation }
                .Where(part => !string.IsNullOrWhiteSpace(part)));

    public string MetaLine(int entryCount)
    {
        var parts = new[] { AgeLabel, Server, Occupation }
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .ToList();

        parts.Add(entryCount == 1 ? "1 entry" : $"{entryCount} entries");
        return string.Join(" · ", parts);
    }

    public bool Matches(string search)
    {
        if (string.IsNullOrWhiteSpace(search))
        {
            return true;
        }

        var needle = search.Trim();
        return Contains(Name) || Contains(Nickname) || Contains(Server);

        bool Contains(string? value) =>
            value is not null &&
            value.Contains(needle, StringComparison.OrdinalIgnoreCase);
    }
}
