namespace OpenShock.RepositoryServer.Models.Firmware;

/// <summary>
/// Full release response including release notes and all boards. Used by both the
/// latest endpoint and the specific-version endpoint — same type, same shape.
/// </summary>
public sealed record FirmwareReleaseDto
{
    public required string Version { get; init; }
    public required string Channel { get; init; }
    public required DateTimeOffset ReleaseDate { get; init; }
    public required FirmwareSourceDto Source { get; init; }
    public required List<FirmwareReleaseNoteDto> ReleaseNotes { get; init; }
    public required Dictionary<string, FirmwareBoardDetailDto> Boards { get; init; }
}
