namespace OpenShock.RepositoryServer.Models.Firmware;

/// <summary>
/// Minimal per-board response for a single version. No release notes, no chip info,
/// no other boards — just what a hub needs to download and flash.
/// </summary>
public sealed record FirmwareBoardReleaseResponseDto
{
    public required string Version { get; init; }
    public required Guid BoardId { get; init; }
    public required List<FirmwareArtifactDto> Artifacts { get; init; }
}
