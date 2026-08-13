using OpenShock.RepositoryServer.Enums;

namespace OpenShock.RepositoryServer.RepoServerDb;

public class FirmwareChip
{
    public Guid Id { get; set; }

    /// <summary>
    /// Display name — must match esptool-js chip identifiers exactly
    /// (e.g. <c>"ESP32"</c>, <c>"ESP32-S3"</c>, <c>"ESP32-C3"</c>). Unique.
    /// </summary>
    public string Name { get; set; } = null!;

    public FirmwareChipArchitecture? Architecture { get; set; }

    public virtual ICollection<FirmwareBoard> Boards { get; set; } = new List<FirmwareBoard>();
    public virtual ICollection<UsbDevice> UsbDevices { get; set; } = new List<UsbDevice>();
}
