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

    [Required]
    [MinLength(1)]
    public required List<string> Boards { get; init; }

    /// <summary>
    /// Markdown changelog per firmware-api-spec.md §5.3. The server parses this into
    /// structured <see cref="FirmwareReleaseNoteDto"/> entries. Empty / whitespace-only
    /// strings reach the parser and yield <c>firmware/invalid-changelog</c>.
    /// </summary>
    [Required(AllowEmptyStrings = true)]
    public required string Changelog { get; init; }
}
