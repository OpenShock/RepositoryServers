using System.ComponentModel.DataAnnotations;

namespace OpenShock.RepositoryServer.Config;

public class ApiConfig
{
    [Required] public required DbConfig Db { get; init; }
    [Required] public required string AdminToken { get; init; }
    [Required] public required RepoConfig Repo { get; init; }
    [Required] public required FirmwareConfig Firmware { get; init; }

    /// <summary>
    /// Shared CI/CD auth settings. Deliberately not nested under <see cref="Firmware"/>: the scheme it
    /// configures also guards desktop module publishing.
    /// </summary>
    [Required] public required CiCdConfig CiCd { get; init; }
    public MetricsConfig Metrics { get; init; } = new();
    public DiscordConfig Discord { get; init; } = new();
}