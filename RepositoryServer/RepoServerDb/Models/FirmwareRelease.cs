using OpenShock.RepositoryServer.Enums;

namespace OpenShock.RepositoryServer.RepoServerDb.Models;

public sealed class FirmwareRelease
{
    public Guid Id { get; set; }
    public required string Version { get; set; }
    public required ReleaseChannel Channel { get; set; }
    public required Guid RepositoryId { get; set; }
    public required string CommitHash { get; set; }
    public string? Ref { get; set; }
    public string? RunId { get; set; }
    public required DateTimeOffset ReleaseDate { get; set; }
    public required ReleaseStatus Status { get; set; }
    public Guid[] DeclaredBoards { get; set; } = [];
    public required DateTimeOffset CreatedAt { get; set; }

    public SourceRepository RepositoryNavigation { get; set; } = null!;
    public ICollection<FirmwareStagedArtifact> StagedArtifacts { get; } = [];
    public ICollection<FirmwareStagedReleaseNote> StagedReleaseNotes { get; } = [];
}
