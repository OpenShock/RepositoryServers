using OpenShock.RepositoryServer.Enums;

namespace OpenShock.RepositoryServer.RepoServerDb;

public class FirmwareStagedArtifact
{
    public Guid ReleaseId { get; set; }
    public string BoardId { get; set; } = null!;
    public FirmwareArtifactType ArtifactType { get; set; }
    public byte[] HashSha256 { get; set; } = null!;
    public long FileSize { get; set; }

    public virtual FirmwareRelease ReleaseNavigation { get; set; } = null!;
}
