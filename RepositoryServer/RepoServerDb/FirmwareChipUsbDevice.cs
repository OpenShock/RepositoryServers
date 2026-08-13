namespace OpenShock.RepositoryServer.RepoServerDb;

/// <summary>
/// Junction linking a firmware chip to a USB device entry. Represents native-USB modes
/// inherent to the silicon (e.g. ESP32-S3 USB-JTAG).
/// </summary>
public class FirmwareChipUsbDevice
{
    public Guid ChipId { get; set; }
    public Guid UsbDeviceId { get; set; }

    public virtual FirmwareChip Chip { get; set; } = null!;
    public virtual UsbDevice UsbDevice { get; set; } = null!;
}
