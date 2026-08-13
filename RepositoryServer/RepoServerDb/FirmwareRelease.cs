using OpenShock.RepositoryServer.Enums;

namespace OpenShock.RepositoryServer.RepoServerDb;

public class FirmwareRelease
{
    public Guid Id { get; set; }
    public string Version { get; set; } = null!;
    public ReleaseChannel Channel { get; set; }
    public Guid RepositoryId { get; set; }
    public string CommitHash { get; set; } = null!;
    public string? Ref { get; set; }
    public string? RunId { get; set; }
    public DateTimeOffset ReleaseDate { get; set; }
    public ReleaseStatus Status { get; set; }
    public Guid[] DeclaredBoards { get; set; } = [];
    public DateTimeOffset CreatedAt { get; set; }

    public virtual SourceRepository RepositoryNavigation { get; set; } = null!;
    public virtual ICollection<FirmwareStagedArtifact> StagedArtifacts { get; set; } = new List<FirmwareStagedArtifact>();
    public virtual ICollection<FirmwareStagedReleaseNote> StagedReleaseNotes { get; set; } = new List<FirmwareStagedReleaseNote>();
}
