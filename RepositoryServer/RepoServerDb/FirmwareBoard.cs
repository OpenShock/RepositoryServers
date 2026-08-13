using OpenShock.RepositoryServer.Enums;

namespace OpenShock.RepositoryServer.RepoServerDb;

public class FirmwareBoard
{
    public Guid Id { get; set; }

    /// <summary>
    /// Human-readable board name (e.g. <c>"OpenShock Core V1"</c>). Unique.
    /// </summary>
    public string Name { get; set; } = null!;

    public Guid ChipId { get; set; }
    public bool Discontinued { get; set; }
    public FirmwareArtifactType[] RequiredArtifactTypes { get; set; } = [];

    public virtual FirmwareChip ChipNavigation { get; set; } = null!;
    public virtual ICollection<FirmwareArtifact> Artifacts { get; set; } = new List<FirmwareArtifact>();
    public virtual ICollection<UsbDevice> UsbDevices { get; set; } = new List<UsbDevice>();
}
