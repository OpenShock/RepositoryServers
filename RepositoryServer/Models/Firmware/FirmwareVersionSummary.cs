namespace OpenShock.RepositoryServer.Models.Firmware;

/// <summary>
/// Version summary for paginated listings. Includes source traceability and release notes
/// so consumers don't need per-version follow-up requests.
/// </summary>
public sealed record FirmwareVersionSummary
{
    public required string Version { get; init; }
    public required string Channel { get; init; }
    public required DateTimeOffset ReleaseDate { get; init; }
    public required FirmwareSourceDto Source { get; init; }
    public List<FirmwareReleaseNoteDto> ReleaseNotes { get; init; } = new();
}
