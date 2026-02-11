namespace OpenShock.RepositoryServer.RepoServerDb;

public class FirmwareVersion
{
    public string Version { get; set; } = null!;
    public FirmwareChannel Channel { get; set; }
    public DateTimeOffset ReleaseDate { get; set; }
    public string CommitHash { get; set; } = null!;
    public string? ReleaseUrl { get; set; }

    public virtual ICollection<FirmwareArtifact> Artifacts { get; set; } = new List<FirmwareArtifact>();
    public virtual ICollection<FirmwareReleaseNote> ReleaseNotes { get; set; } = new List<FirmwareReleaseNote>();
}
