namespace OpenShock.RepositoryServer.RepoServerDb;

/// <summary>
/// Junction linking a firmware board to a USB device entry. Represents on-board USB-serial
/// converters or board-unique VID/PIDs (e.g. a Wemos variant shipped with a CP2104).
/// </summary>
public class FirmwareBoardUsbDevice
{
    public string BoardId { get; set; } = null!;
    public Guid UsbDeviceId { get; set; }

    public virtual FirmwareBoard Board { get; set; } = null!;
    public virtual UsbDevice UsbDevice { get; set; } = null!;
}
