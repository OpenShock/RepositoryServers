namespace OpenShock.RepositoryServer.RepoServerDb;

public class FirmwareBoard
{
    public string Id { get; set; } = null!;
    public string ChipId { get; set; } = null!;
    public string Name { get; set; } = null!;
    public bool Discontinued { get; set; }

    public virtual FirmwareChip ChipNavigation { get; set; } = null!;
    public virtual ICollection<FirmwareArtifact> Artifacts { get; set; } = new List<FirmwareArtifact>();
}
