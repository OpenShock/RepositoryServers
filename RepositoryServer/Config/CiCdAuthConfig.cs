using System.ComponentModel.DataAnnotations;

namespace OpenShock.RepositoryServer.Config;

public sealed class CiCdAuthConfig
{
    /// <summary>
    /// Expected audience claim in the GitHub OIDC token.
    /// Set this to match the 'audience' parameter in your GitHub Actions workflow.
    /// </summary>
    [Required(AllowEmptyStrings = false)]
    public required string Audience { get; init; }

    /// <summary>
    /// GitHub organization or user that owns the repositories allowed to authenticate.
    /// Validated against the 'repository_owner' claim in the OIDC token.
    /// </summary>
    [Required(AllowEmptyStrings = false)]
    public required string RepositoryOwner { get; init; }

    /// <summary>
    /// Optional list of specific repositories allowed to authenticate (e.g. "openshock/firmware").
    /// If null or empty, any repository under the owner is allowed.
    /// </summary>
    public List<string>? AllowedRepositories { get; init; }
}
