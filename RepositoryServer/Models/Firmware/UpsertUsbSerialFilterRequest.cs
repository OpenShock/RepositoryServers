using System.ComponentModel.DataAnnotations;

namespace OpenShock.RepositoryServer.Models.Firmware;

public sealed class UpsertUsbSerialFilterRequest
{
    [Range(0, 0xFFFF)] public required int Vid { get; init; }
    [Range(0, 0xFFFF)] public int? Pid { get; init; }
    [MaxLength(256)] public string? Description { get; init; }
}
