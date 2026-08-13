namespace OpenShock.RepositoryServer.Models.Firmware;

/// <summary>
/// Slim chip reference embedded in per-board release responses.
/// </summary>
/// <remarks>
/// <see cref="Name"/> is the chip's public identifier. It is unique, and pinned externally: it must
/// match esptool-js chip identifiers exactly (e.g. <c>"ESP32-S3"</c>), because the flashtool passes it
/// straight through. The chip's UUID stays internal — unlike a board, a chip never appears in a storage
/// path, so there is nothing an immutable surrogate key would buy here.
/// </remarks>
public sealed record FirmwareChipRefDto
{
    public required string Name { get; init; }
}
