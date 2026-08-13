namespace OpenShock.RepositoryServer.Models.Firmware;

/// <summary>
/// Admin-only view of a USB serial filter row, including id + description.
/// Never surfaced on the public manifest.
/// </summary>
public sealed record FirmwareUsbSerialFilterAdminDto
{
    public required Guid Id { get; init; }
    public required int Vid { get; init; }
    public int? Pid { get; init; }
    public string? Description { get; init; }
}
