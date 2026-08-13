using OpenShock.RepositoryServer.Enums;

namespace OpenShock.RepositoryServer.RepoServerDb.Models;

public sealed class FirmwareBoard
{
    public Guid Id { get; set; }

    /// <summary>
    /// Human-readable board name (e.g. <c>"OpenShock Core V1"</c>). Unique.
    /// </summary>
    public required string Name { get; set; }

    public required Guid ChipId { get; set; }
    public bool Discontinued { get; set; }
    public FirmwareArtifactType[] RequiredArtifactTypes { get; set; } = [];

    public FirmwareChip ChipNavigation { get; set; } = null!;
    public ICollection<FirmwareArtifact> Artifacts { get; } = [];
    public ICollection<UsbDevice> UsbDevices { get; } = [];
}
