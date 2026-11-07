using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace RoleplayDiary;

/// <summary>
/// PDF export: a character from beginning to end.
///
/// The page is designed for print and for sharing in a forum thread, so unlike the
/// app it is light with dark text.
/// </summary>
public sealed class PdfExporter
{
    // Print colours. Deliberately not the app's dark theme.
    private const string Ink = "#15171D";
    private const string Body = "#33384A";
    private const string Muted = "#767D90";
    private const string Rule = "#DADEE7";
    private const string Accent = "#5B3FD9";

    static PdfExporter()
    {
        // QuestPDF is free under its Community licence for individuals and small
        // organisations. Declaring it is required before generating anything.
        QuestPDF.Settings.License = LicenseType.Community;
    }

    private static string StatusColour(CharacterStatus status) => status switch
    {
        CharacterStatus.Alive => "#1C8B5B",
        CharacterStatus.Dead => "#C2314A",
        CharacterStatus.Shelved => "#B07214",
        _ => Muted
    };

    /// <summary>A file name Windows will accept, whatever the character is called.</summary>
    public static string SuggestedFileName(string characterName)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(characterName
            .Select(ch => invalid.Contains(ch) ? '_' : ch)
            .ToArray())
            .Trim()
            .TrimEnd('.');

        return $"{(cleaned.Length > 0 ? cleaned : "Character")} - Roleplay Diary.pdf";
    }

    public string Export(CharacterArchive archive, string destination)
    {
        var folder = Path.GetDirectoryName(destination);
        if (!string.IsNullOrEmpty(folder))
        {
            Directory.CreateDirectory(folder);
        }

        Document.Create(document =>
        {
            document.Page(page => ComposeCover(page, archive));
            document.Page(page => ComposeBody(page, archive));
        }).GeneratePdf(destination);

        return destination;
    }

    private static void StandardPage(PageDescriptor page)
    {
        page.Size(PageSizes.A4);
        page.Margin(18, Unit.Millimetre);
        page.DefaultTextStyle(style => style.FontSize(10).FontColor(Body).LineHeight(1.45f));
    }

    // ---------------------------------------------------------------- cover

    private static void ComposeCover(PageDescriptor page, CharacterArchive archive)
    {
        StandardPage(page);
        var character = archive.Character;

        page.Content().PaddingTop(14, Unit.Millimetre).Column(column =>
        {
            if (Readable(archive.PortraitPath))
            {
                column.Item()
                    .AlignCenter()
                    .MaxWidth(110, Unit.Millimetre)
                    .MaxHeight(105, Unit.Millimetre)
                    .Image(archive.PortraitPath!)
                    .FitArea();

                column.Item().Height(12, Unit.Millimetre);
            }
            else
            {
                column.Item().Height(30, Unit.Millimetre);
            }

            column.Item().AlignCenter().Text(character.Name)
                .FontSize(30).Bold().FontColor(Ink);

            if (!string.IsNullOrWhiteSpace(character.Nickname))
            {
                column.Item().PaddingTop(2, Unit.Millimetre).AlignCenter()
                    .Text(character.QuotedNickname).FontSize(14).FontColor(Muted);
            }

            var facts = Join(character.AgeLabel, character.Server, character.Occupation);
            if (facts.Length > 0)
            {
                column.Item().PaddingTop(5, Unit.Millimetre).AlignCenter()
                    .Text(facts).FontColor(Muted);
            }

            column.Item().PaddingTop(4, Unit.Millimetre).AlignCenter()
                .Text(character.Status.Label())
                .Bold().FontColor(StatusColour(character.Status));

            var tail = Join(archive.SpanLabel, character.ArchivedLabel);
            column.Item().PaddingTop(3, Unit.Millimetre).AlignCenter()
                .Text(tail).FontColor(Muted);
        });

        // The cover deliberately carries no page number.
        page.Footer().Text("Roleplay Diary").FontSize(7.5f).FontColor(Muted);
    }

    // ----------------------------------------------------------------- body

    private static void ComposeBody(PageDescriptor page, CharacterArchive archive)
    {
        StandardPage(page);

        page.Content().Column(column =>
        {
            Heading(column, "Profile", first: true);
            Profile(column, archive.Character);

            if (!string.IsNullOrWhiteSpace(archive.Character.Background))
            {
                Heading(column, "Background");
                column.Item().PaddingTop(3, Unit.Millimetre)
                    .Text(archive.Character.Background);
            }

            Heading(column, $"Timeline · {archive.Entries.Count} entries");
            Timeline(column, archive);

            if (archive.Photos.Count > 0)
            {
                Heading(column, $"Gallery · {archive.Photos.Count} photos");
                Gallery(column, archive);
            }
        });

        page.Footer().Row(row =>
        {
            row.RelativeItem().Text("Roleplay Diary").FontSize(7.5f).FontColor(Muted);
            row.ConstantItem(60).AlignRight().Text(text =>
            {
                text.DefaultTextStyle(style => style.FontSize(7.5f).FontColor(Muted));
                text.CurrentPageNumber();
            });
        });
    }

    private static void Heading(ColumnDescriptor column, string text, bool first = false)
    {
        if (!first)
        {
            column.Item().Height(9, Unit.Millimetre);
        }

        column.Item().Text(text.ToUpperInvariant())
            .FontSize(8).Bold().FontColor(Accent);

        column.Item().PaddingTop(2).LineHorizontal(0.6f).LineColor(Rule);
    }

    private static void Profile(ColumnDescriptor column, Character character)
    {
        var fields = new (string Label, string? Value)[]
        {
            ("Name", character.Name),
            ("Nickname", character.Nickname),
            ("Age", character.Age?.ToString()),
            ("Status", character.Status.Label()),
            ("Server or community", character.Server),
            ("Occupation", character.Occupation),
            ("Faction or affiliation", character.Affiliation)
        };

        column.Item().PaddingTop(4, Unit.Millimetre).Column(table =>
        {
            foreach (var (label, value) in fields.Where(f => !string.IsNullOrWhiteSpace(f.Value)))
            {
                table.Item().PaddingVertical(3).Row(row =>
                {
                    row.ConstantItem(42, Unit.Millimetre)
                        .Text(label).FontSize(8).Bold().FontColor(Muted);
                    row.RelativeItem().Text(value!);
                });
            }
        });
    }

    private static void Timeline(ColumnDescriptor column, CharacterArchive archive)
    {
        if (archive.Entries.Count == 0)
        {
            column.Item().PaddingTop(4, Unit.Millimetre)
                .Text("No entries yet.").FontSize(8).FontColor(Muted);
            return;
        }

        foreach (var entry in archive.Entries)
        {
            column.Item().PaddingTop(5, Unit.Millimetre).Row(row =>
            {
                row.ConstantItem(34, Unit.Millimetre).Column(stamp =>
                {
                    stamp.Item().Text(entry.ShortDateLabel).FontSize(8).Bold().FontColor(Ink);
                    stamp.Item().Text(entry.KindLabel).FontSize(8).FontColor(Muted);
                });

                row.RelativeItem()
                    .BorderLeft(0.8f).BorderColor(Rule)
                    .PaddingLeft(6)
                    .Column(story =>
                    {
                        story.Item().Text(entry.Title).FontSize(12).Bold().FontColor(Ink);

                        if (!string.IsNullOrWhiteSpace(entry.Body))
                        {
                            story.Item().PaddingTop(1.5f, Unit.Millimetre).Text(entry.Body);
                        }
                    });
            });
        }
    }

    private static void Gallery(ColumnDescriptor column, CharacterArchive archive)
    {
        var usable = archive.Photos.Where(photo => Readable(photo.FullPath)).ToList();
        if (usable.Count == 0)
        {
            return;
        }

        column.Item().Height(4, Unit.Millimetre);

        foreach (var pair in usable.Chunk(2))
        {
            column.Item().PaddingBottom(4, Unit.Millimetre).Row(row =>
            {
                row.Spacing(6, Unit.Millimetre);

                foreach (var photo in pair)
                {
                    row.RelativeItem().Column(cell =>
                    {
                        cell.Item().MaxHeight(58, Unit.Millimetre)
                            .Image(photo.FullPath!).FitArea();

                        if (!string.IsNullOrWhiteSpace(photo.Caption))
                        {
                            cell.Item().PaddingTop(1.5f, Unit.Millimetre)
                                .Text(photo.Caption).FontSize(8).FontColor(Muted);
                        }
                    });
                }

                // An odd photo out still needs a second column, or it stretches wide.
                if (pair.Length == 1)
                {
                    row.RelativeItem();
                }
            });
        }
    }

    // --------------------------------------------------------------- helpers

    private static bool Readable(string? path) =>
        !string.IsNullOrWhiteSpace(path) && File.Exists(path);

    private static string Join(params string?[] parts) =>
        string.Join(" · ", parts.Where(part => !string.IsNullOrWhiteSpace(part)));
}
