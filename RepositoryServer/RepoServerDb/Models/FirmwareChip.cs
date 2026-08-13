using OpenShock.RepositoryServer.Enums;

namespace OpenShock.RepositoryServer.RepoServerDb.Models;

public sealed class FirmwareChip
{
    public Guid Id { get; set; }

    /// <summary>
    /// Display name — must match esptool-js chip identifiers exactly
    /// (e.g. <c>"ESP32"</c>, <c>"ESP32-S3"</c>, <c>"ESP32-C3"</c>). Unique.
    /// </summary>
    public required string Name { get; set; }

    public required FirmwareChipArchitecture? Architecture { get; set; }

    public ICollection<FirmwareBoard> Boards { get; } = [];
    public ICollection<UsbDevice> UsbDevices { get; } = [];
}
