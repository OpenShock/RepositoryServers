using System.ComponentModel.DataAnnotations;

namespace OpenShock.RepositoryServer.Models.Firmware;

public sealed class InitReleaseRequest
{
    [Required(AllowEmptyStrings = false)]
    public required string Version { get; init; }

    [Required(AllowEmptyStrings = false)]
    public required string Channel { get; init; }

    [Required]
    public required DateTimeOffset ReleaseDate { get; init; }

    /// <summary>
    /// Boards this release will publish artifacts for, by name (e.g. <c>"Wemos-D1-Mini-ESP32"</c>) —
    /// the same identifier CI already knows as the PlatformIO env. Board UUIDs are also accepted.
    /// </summary>
    [Required]
    [MinLength(1)]
    public required List<string> Boards { get; init; }

    /// <summary>
    /// Markdown changelog. The server parses this into structured
    /// <see cref="FirmwareReleaseNoteDto"/> entries; see <c>ChangelogParser</c> for the accepted
    /// grammar. Empty / whitespace-only strings reach the parser and yield
    /// <c>Firmware.InvalidChangelog</c>.
    /// </summary>
    [Required(AllowEmptyStrings = true)]
    public required string Changelog { get; init; }
}
