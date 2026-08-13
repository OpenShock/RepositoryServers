using OpenShock.RepositoryServer.Enums;

namespace OpenShock.RepositoryServer.RepoServerDb.Models;

public sealed class FirmwareArtifact
{
    public required string Version { get; set; }
    public required Guid BoardId { get; set; }
    public required FirmwareArtifactType ArtifactType { get; set; }
    public required byte[] HashSha256 { get; set; }
    public required long FileSize { get; set; }

    public FirmwareVersion VersionNavigation { get; set; } = null!;
    public FirmwareBoard BoardNavigation { get; set; } = null!;
}
