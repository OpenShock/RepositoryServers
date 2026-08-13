namespace OpenShock.RepositoryServer.Models.Firmware;

/// <summary>
/// Chip summary used by the manifest and the public chips listing. Carries chip-level
/// native-USB identities inline (e.g. ESP32-S3 USB-JTAG). Boards inherit these by matching
/// <c>FirmwareBoardDto.ChipName</c> against <see cref="Name"/> — they are not duplicated on the
/// board DTO.
/// </summary>
public sealed record FirmwareChipDto
{
    public required string Name { get; init; }
    public string? Architecture { get; init; }
    public List<FirmwareUsbDeviceDto> UsbDevices { get; init; } = new();
}
