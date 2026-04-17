namespace OpenShock.RepositoryServer.Models.Firmware;

/// <summary>
/// Recognition catalog entry — specific VID+PID pair with human-readable name.
/// Consumed by the manifest <c>usbDevices</c> list and by each board's/chip's inline
/// <c>usbDevices</c> array.
/// </summary>
public sealed record FirmwareUsbDeviceDto
{
    public required Guid Id { get; init; }
    public required int Vid { get; init; }
    public required int Pid { get; init; }
    public required string Name { get; init; }
}
