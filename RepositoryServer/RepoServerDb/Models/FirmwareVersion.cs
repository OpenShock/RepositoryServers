using OpenShock.RepositoryServer.Enums;

namespace OpenShock.RepositoryServer.RepoServerDb.Models;

public sealed class FirmwareVersion
{
    public required string Version { get; set; }
    public required ReleaseChannel Channel { get; set; }
    public required DateTimeOffset ReleaseDate { get; set; }
    public required Guid RepositoryId { get; set; }
    public required string CommitHash { get; set; }
    public string? Ref { get; set; }
    public string? RunId { get; set; }

    public SourceRepository RepositoryNavigation { get; set; } = null!;
    public ICollection<FirmwareArtifact> Artifacts { get; } = [];
    public ICollection<FirmwareReleaseNote> ReleaseNotes { get; } = [];
}
