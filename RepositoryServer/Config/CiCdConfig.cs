using System.ComponentModel.DataAnnotations;

namespace OpenShock.RepositoryServer.Config;

/// <summary>
/// Settings for the shared CI/CD authentication scheme, used by both firmware release ingestion and
/// desktop module publishing.
/// </summary>
public sealed class CiCdConfig
{
    /// <summary>
    /// GitHub OIDC audience this server accepts. A workflow chooses its own <c>audience</c> when it
    /// requests a token, and tokens not addressed here are rejected.
    /// </summary>
    /// <remarks>
    /// This is what makes an OIDC token non-transferable between services. <c>id-token: write</c> is
    /// granted per job, so any action in a publishing workflow can mint a token; without this check,
    /// a token obtained for some unrelated vendor would still be a valid firmware-publishing
    /// credential, because it carries the same <c>repository</c> claim.
    ///
    /// It is an identifier, not a secret — anyone can request it by name — so it authenticates nothing
    /// about <em>who</em> is calling. That is the allowlist's job, and scopes decide what they may do.
    /// </remarks>
    [Required(AllowEmptyStrings = false)]
    public required string Audience { get; init; }
}
