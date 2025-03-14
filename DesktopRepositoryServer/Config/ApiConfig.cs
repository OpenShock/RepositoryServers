using System.ComponentModel.DataAnnotations;

namespace OpenShock.Desktop.RepositoryServer.Config;

public class ApiConfig
{
    [Required] public required DbConfig Db { get; init; }
    [Required] public required string AdminToken { get; init; }
    [Required] public required RepoConfig Repo { get; init; }
    public MetricsConfig Metrics { get; init; } = new();
}