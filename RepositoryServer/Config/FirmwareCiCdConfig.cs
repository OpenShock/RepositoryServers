using System.ComponentModel.DataAnnotations;

namespace OpenShock.RepositoryServer.Config;

public sealed class FirmwareCiCdConfig
{
    /// <summary>
    /// Expected audience claim in the GitHub OIDC token.
    /// Set this to match the 'audience' parameter in your GitHub Actions workflow.
    /// </summary>
    [Required(AllowEmptyStrings = false)]
    public required string Audience { get; init; }
}
