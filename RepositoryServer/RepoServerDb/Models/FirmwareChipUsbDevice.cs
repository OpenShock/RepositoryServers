namespace OpenShock.RepositoryServer.RepoServerDb.Models;

/// <summary>
/// Junction linking a firmware chip to a USB device entry. Represents native-USB modes
/// inherent to the silicon (e.g. ESP32-S3 USB-JTAG).
/// </summary>
public sealed class FirmwareChipUsbDevice
{
    public required Guid ChipId { get; set; }
    public required Guid UsbDeviceId { get; set; }

    public FirmwareChip Chip { get; set; } = null!;
    public UsbDevice UsbDevice { get; set; } = null!;
}
