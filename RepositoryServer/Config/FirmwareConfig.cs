using System.ComponentModel.DataAnnotations;

namespace OpenShock.RepositoryServer.Config;

public sealed class FirmwareConfig
{
    [Required(AllowEmptyStrings = false)] public required string CdnBaseUrl { get; init; }
    [Required] public required StorageConfig Storage { get; init; }
}
