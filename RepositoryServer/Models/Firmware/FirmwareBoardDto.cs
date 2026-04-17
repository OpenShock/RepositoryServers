namespace OpenShock.RepositoryServer.Models.Firmware;

/// <summary>
/// Board summary used by the manifest and the public boards listing. Carries board-specific
/// USB identities inline (on-board USB-serial converters, board-unique VID/PIDs). Chip-level
/// native-USB entries are NOT duplicated here — consumers inherit them via <c>chipId</c>.
/// </summary>
public sealed record FirmwareBoardDto
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public required Guid ChipId { get; init; }
    public required string ChipName { get; init; }
    public required bool Discontinued { get; init; }
    public List<FirmwareUsbDeviceDto> UsbDevices { get; init; } = new();
}
