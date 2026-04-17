using System.ComponentModel.DataAnnotations;

namespace OpenShock.RepositoryServer.Models.Firmware;

public sealed class UpsertUsbDeviceRequest
{
    [Range(0, 0xFFFF)] public required int Vid { get; init; }
    [Range(0, 0xFFFF)] public required int Pid { get; init; }
    [Required(AllowEmptyStrings = false)][MaxLength(128)] public required string Name { get; init; }
}
