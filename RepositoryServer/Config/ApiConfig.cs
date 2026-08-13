using System.ComponentModel.DataAnnotations;

namespace OpenShock.RepositoryServer.Config;

public class ApiConfig
{
    [Required] public required DbConfig Db { get; init; }

    /// <summary>
    /// Admin authentication. Admin endpoints accept nothing else, so a server without this section has
    /// no reachable admin surface at all.
    /// </summary>
    /// <remarks>
    /// Nullable only so local development can run without an Authentik instance. Startup rejects a
    /// missing section unless <see cref="DevAuthConfig.BypassAuthentik"/> is active, which cannot
    /// happen outside a Development-environment Debug build.
    /// </remarks>
    public AuthentikConfig? Authentik { get; init; }

    /// <summary>Development-only overrides. Has no effect in a published build.</summary>
    public DevAuthConfig DevAuth { get; init; } = new();

    [Required] public required RepoConfig Repo { get; init; }
    [Required] public required FirmwareConfig Firmware { get; init; }

    /// <summary>
    /// Shared CI/CD auth settings. Deliberately not nested under <see cref="Firmware"/>: the scheme it
    /// configures also guards desktop module publishing.
    /// </summary>
    [Required] public required CiCdConfig CiCd { get; init; }
    public MetricsConfig Metrics { get; init; } = new();
}