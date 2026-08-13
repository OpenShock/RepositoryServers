namespace OpenShock.RepositoryServer.RepoServerDb.Models;

/// <summary>
/// Recognition catalog entry — a specific USB VID+PID pair with a human-readable name.
/// Linked to firmware chips (native-USB modes) and boards (on-board USB-serial converters)
/// via <see cref="FirmwareChipUsbDevice"/> and <see cref="FirmwareBoardUsbDevice"/> junctions.
/// </summary>
public sealed class UsbDevice
{
    public Guid Id { get; set; }
    public required int Vid { get; set; }
    public required int Pid { get; set; }
    public required string Name { get; set; }

    public ICollection<FirmwareChip> Chips { get; } = [];
    public ICollection<FirmwareBoard> Boards { get; } = [];
}
