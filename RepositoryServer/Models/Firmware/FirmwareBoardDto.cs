namespace OpenShock.RepositoryServer.Models.Firmware;

/// <summary>
/// Board summary used by the manifest and the public boards listing. Carries board-specific
/// USB identities inline (on-board USB-serial converters, board-unique VID/PIDs). Chip-level
/// native-USB entries are NOT duplicated here — consumers inherit them via <c>chipName</c>.
/// </summary>
public sealed record FirmwareBoardDto
{
    /// <summary>Canonical board name — the public identifier, e.g. <c>"Wemos-D1-Mini-ESP32"</c>.</summary>
    public required string Name { get; init; }

    /// <summary>Chip name, joining this board to an entry in the manifest's <c>chips</c> array.</summary>
    public required string ChipName { get; init; }
    public required bool Discontinued { get; init; }
    public List<FirmwareUsbDeviceDto> UsbDevices { get; init; } = new();
}
