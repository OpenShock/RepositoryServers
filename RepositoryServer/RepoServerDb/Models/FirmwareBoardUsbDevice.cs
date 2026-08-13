namespace OpenShock.RepositoryServer.RepoServerDb.Models;

/// <summary>
/// Junction linking a firmware board to a USB device entry. Represents on-board USB-serial
/// converters or board-unique VID/PIDs (e.g. a Wemos variant shipped with a CP2104).
/// </summary>
public sealed class FirmwareBoardUsbDevice
{
    public required Guid BoardId { get; set; }
    public required Guid UsbDeviceId { get; set; }

    public FirmwareBoard Board { get; set; } = null!;
    public UsbDevice UsbDevice { get; set; } = null!;
}
