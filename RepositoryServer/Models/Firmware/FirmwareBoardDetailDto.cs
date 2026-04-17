namespace OpenShock.RepositoryServer.Models.Firmware;

/// <summary>
/// Per-board details within a release response: chip info, discontinuation flag,
/// and all artifacts available for the board in the given version.
/// </summary>
public sealed record FirmwareBoardDetailDto
{
    public required FirmwareChipRefDto Chip { get; init; }
    public required bool Discontinued { get; init; }
    public required List<FirmwareArtifactDto> Artifacts { get; init; }
}
