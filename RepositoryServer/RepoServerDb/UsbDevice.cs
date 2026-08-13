namespace OpenShock.RepositoryServer.RepoServerDb;

/// <summary>
/// Recognition catalog entry — a specific USB VID+PID pair with a human-readable name.
/// Linked to firmware chips (native-USB modes) and boards (on-board USB-serial converters)
/// via <see cref="FirmwareChipUsbDevice"/> and <see cref="FirmwareBoardUsbDevice"/> junctions.
/// </summary>
public class UsbDevice
{
    public Guid Id { get; set; }
    public int Vid { get; set; }
    public int Pid { get; set; }
    public string Name { get; set; } = null!;

    public virtual ICollection<FirmwareChip> Chips { get; set; } = new List<FirmwareChip>();
    public virtual ICollection<FirmwareBoard> Boards { get; set; } = new List<FirmwareBoard>();
}
