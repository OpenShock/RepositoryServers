namespace OpenShock.RepositoryServer.Models.Firmware;

public sealed class FirmwareArtifactUpload
{
    public required string Sha256Hash { get; init; }
    public required long FileSize { get; init; }
}
