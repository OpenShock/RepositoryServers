namespace OpenShock.RepositoryServer.Models.Firmware;

/// <summary>
/// Bootstrap payload returned by <c>GET /v2/firmware/manifest</c>. Aggregates channels,
/// latest version per channel, boards, chips, USB device filters, and security advisories.
/// </summary>
public sealed record FirmwareManifestResponse
{
    public required List<string> Channels { get; init; }
    public required Dictionary<string, string> Latest { get; init; }
    public required List<FirmwareBoardDto> Boards { get; init; }
    public required List<FirmwareChipDto> Chips { get; init; }
    public required List<FirmwareUsbSerialFilterDto> UsbSerialFilters { get; init; }
    public required List<FirmwareUsbDeviceDto> UsbDevices { get; init; }
    public required List<FirmwareAdvisoryDto> Advisories { get; init; }
}
