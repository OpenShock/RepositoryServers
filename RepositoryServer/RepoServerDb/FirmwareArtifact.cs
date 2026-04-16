using OpenShock.RepositoryServer.Enums;

namespace OpenShock.RepositoryServer.RepoServerDb;

public class FirmwareArtifact
{
    public string Version { get; set; } = null!;
    public string BoardId { get; set; } = null!;
    public FirmwareArtifactType ArtifactType { get; set; }
    public byte[] HashSha256 { get; set; } = null!;
    public long FileSize { get; set; }

    public virtual FirmwareVersion VersionNavigation { get; set; } = null!;
    public virtual FirmwareBoard BoardNavigation { get; set; } = null!;
}
