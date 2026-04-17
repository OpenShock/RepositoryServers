namespace OpenShock.RepositoryServer.Models.Firmware;

/// <summary>
/// Chip summary used by the manifest and the public chips listing. Carries chip-level
/// native-USB identities inline (e.g. ESP32-S3 USB-JTAG). Boards inherit these by virtue
/// of their <c>chipId</c> — they are not duplicated on the board DTO.
/// </summary>
public sealed record FirmwareChipDto
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string? Architecture { get; init; }
    public List<FirmwareUsbDeviceDto> UsbDevices { get; init; } = new();
}
