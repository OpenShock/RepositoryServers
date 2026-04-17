using System.ComponentModel.DataAnnotations;

namespace OpenShock.RepositoryServer.Models.Firmware;

public sealed class CreateFirmwareChipRequest
{
    /// <summary>Must match esptool-js chip identifiers exactly (e.g. <c>"ESP32-S3"</c>).</summary>
    [Required(AllowEmptyStrings = false)]
    [MaxLength(64)]
    public required string Name { get; init; }

    public string? Architecture { get; init; }
}
