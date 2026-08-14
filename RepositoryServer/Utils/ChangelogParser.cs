using OneOf;
using OpenShock.RepositoryServer.Enums;
using OpenShock.RepositoryServer.Models.Firmware;

namespace OpenShock.RepositoryServer.Utils;

public enum ChangelogParseError
{
    Empty,
    NoHeadings,
    AllSectionsEmpty
}

/// <summary>
/// Parses the <c>changelog</c> markdown submitted by CI/CD into structured
/// <see cref="FirmwareReleaseNoteDto"/> entries. Pure function; this class is the definition
/// of the accepted grammar.
/// </summary>
public static class ChangelogParser
{
    public static OneOf<IReadOnlyList<FirmwareReleaseNoteDto>, ChangelogParseError> Parse(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return ChangelogParseError.Empty;
        }

        var lines = input.Replace("\r\n", "\n").Split('\n');
        var notes = new List<FirmwareReleaseNoteDto>();
        var sawHeading = false;

        ReleaseNoteSectionType currentType = ReleaseNoteSectionType.Info;
        string? currentTitle = null;
        var buffer = new List<string>();
        var bullets = new List<string>();

        void AddNote(string item)
        {
            var trimmed = item.Trim();
            if (trimmed.Length == 0) return;

            var (title, content) = ExtractTitle(trimmed, currentTitle);
            notes.Add(new FirmwareReleaseNoteDto
            {
                Type = currentType.ToString().ToLowerInvariant(),
                Title = title,
                Content = content
            });
        }

        void FlushSection()
        {
            // Prose and bullets can coexist in one section: an introductory sentence followed by
            // bullets becomes its own note followed by the bullet notes. Emitting prose first
            // preserves that order.
            if (buffer.Count > 0)
            {
                AddNote(string.Join("\n", buffer.Select(l => l.TrimEnd())));
            }

            foreach (var item in bullets)
            {
                AddNote(item);
            }

            buffer.Clear();
            bullets.Clear();
        }

        foreach (var rawLine in lines)
        {
            var line = rawLine.TrimEnd();

            if (line.StartsWith("### ", StringComparison.Ordinal))
            {
                if (sawHeading)
                {
                    FlushSection();
                }

                sawHeading = true;
                var heading = line[4..].Trim();
                (currentType, currentTitle) = MapHeading(heading);
                continue;
            }

            if (!sawHeading) continue;
            if (string.IsNullOrWhiteSpace(line)) continue;

            // Indented bullets are flattened into siblings rather than dropped. The note DTO has no
            // nesting, and real changelogs use nested lists freely — losing their content silently is
            // worse than losing their depth.
            var unindented = line.TrimStart();
            if (unindented.StartsWith("- ", StringComparison.Ordinal))
            {
                bullets.Add(unindented[2..]);
            }
            else
            {
                buffer.Add(line);
            }
        }

        if (!sawHeading)
        {
            return ChangelogParseError.NoHeadings;
        }

        // Flush the trailing section after the last heading.
        FlushSection();

        if (notes.Count == 0)
        {
            return ChangelogParseError.AllSectionsEmpty;
        }

        IReadOnlyList<FirmwareReleaseNoteDto> readonlyNotes = notes;
        return OneOf<IReadOnlyList<FirmwareReleaseNoteDto>, ChangelogParseError>.FromT0(readonlyNotes);
    }

    private static (ReleaseNoteSectionType type, string? title) MapHeading(string heading) =>
        heading.ToLowerInvariant() switch
        {
            "breaking" => (ReleaseNoteSectionType.Breaking, null),
            "warning" => (ReleaseNoteSectionType.Warning, null),
            "info" => (ReleaseNoteSectionType.Info, null),
            _ => (ReleaseNoteSectionType.Section, heading)
        };

    private static (string? title, string content) ExtractTitle(string item, string? sectionTitle)
    {
        // "**Title** — content" → ("Title", "content")
        // Both em dash (—) and simple hyphen separator " - " are accepted.
        if (!item.StartsWith("**", StringComparison.Ordinal)) return (sectionTitle, item);
        
        var closeIdx = item.IndexOf("**", 2, StringComparison.Ordinal);
        if (closeIdx <= 2) return (sectionTitle, item);
        
        var title = item[2..closeIdx].Trim();
        var rest = item[(closeIdx + 2)..].TrimStart();

        if (rest.StartsWith("—", StringComparison.Ordinal) || rest.StartsWith("–", StringComparison.Ordinal)) rest = rest[1..].TrimStart();
        else if (rest.StartsWith("- ", StringComparison.Ordinal)) rest = rest[2..];
        else if (rest.StartsWith("-", StringComparison.Ordinal)) rest = rest[1..].TrimStart();

        if (title.Length <= 0)
        {
            return (sectionTitle, item);
        }

        return (title, rest);
    }
}
