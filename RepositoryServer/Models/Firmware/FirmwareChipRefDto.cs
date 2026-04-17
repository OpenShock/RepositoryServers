namespace OpenShock.RepositoryServer.Models.Firmware;

/// <summary>
/// Slim chip reference embedded in per-board release responses. The <see cref="Name"/>
/// field must match esptool-js chip identifiers exactly (e.g. <c>"ESP32-S3"</c>).
/// </summary>
public sealed record FirmwareChipRefDto
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
}
