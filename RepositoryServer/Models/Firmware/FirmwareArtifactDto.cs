namespace OpenShock.RepositoryServer.Models.Firmware;

public sealed class FirmwareArtifactDto
{
    public required string Type { get; init; }
    public required string Url { get; init; }
    public required string Sha256Hash { get; init; }
    public required long FileSize { get; init; }
}
