using System.ComponentModel.DataAnnotations;

namespace OpenShock.RepositoryServer.Config;

public sealed class FirmwareConfig
{
    [Required(AllowEmptyStrings = false)] public required string CdnBaseUrl { get; init; }
    [Required] public required StorageConfig Storage { get; init; }
    [Required] public required FirmwareCiCdConfig CiCd { get; init; }

    /// <summary>
    /// How long a release may remain in <c>staging</c> status before the cleanup job aborts it.
    /// </summary>
    public TimeSpan StagedReleaseTtl { get; init; } = TimeSpan.FromHours(1);

    /// <summary>
    /// How long a release may remain in <c>editing</c> status before the cleanup job aborts it.
    /// </summary>
    public TimeSpan EditingReleaseTtl { get; init; } = TimeSpan.FromDays(7);

    /// <summary>
    /// Security / compatibility advisories exposed on the manifest endpoint.
    /// Served from configuration, not from the database.
    /// </summary>
    public List<FirmwareAdvisoryConfig> Advisories { get; init; } = new();
}
