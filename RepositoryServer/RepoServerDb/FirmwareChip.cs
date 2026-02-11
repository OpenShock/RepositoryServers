namespace OpenShock.RepositoryServer.RepoServerDb;

public class FirmwareChip
{
    public string Id { get; set; } = null!;
    public string Name { get; set; } = null!;
    public FirmwareChipArchitecture? Architecture { get; set; }

    public virtual ICollection<FirmwareBoard> Boards { get; set; } = new List<FirmwareBoard>();
}
