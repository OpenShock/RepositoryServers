using OpenShock.RepositoryServer.Enums;

namespace OpenShock.RepositoryServer.RepoServerDb.Models;

public sealed class FirmwareStagedArtifact
{
    public required Guid ReleaseId { get; set; }
    public required Guid BoardId { get; set; }
    public required FirmwareArtifactType ArtifactType { get; set; }
    public required byte[] HashSha256 { get; set; }
    public required long FileSize { get; set; }

    public FirmwareRelease ReleaseNavigation { get; set; } = null!;
}
