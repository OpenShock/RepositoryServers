using System.ComponentModel.DataAnnotations;

namespace OpenShock.RepositoryServer.Models.Firmware;

/// <summary>
/// Registers a source repository as authorized to publish. This is the allowlist entry that
/// <see cref="OpenShock.RepositoryServer.AuthenticationHandlers.GitHubOidcAuthentication"/> checks —
/// a valid GitHub OIDC token alone proves nothing about which repository is calling.
/// </summary>
public sealed class UpsertRepositoryRequest
{
    /// <summary>Currently only <c>github</c> is supported.</summary>
    [Required(AllowEmptyStrings = false)]
    [MaxLength(32)]
    public required string Provider { get; init; }

    /// <summary>Organization or user, matching the OIDC <c>repository_owner</c> claim exactly.</summary>
    [Required(AllowEmptyStrings = false)]
    [MaxLength(128)]
    public required string Owner { get; init; }

    /// <summary>Repository name, matching the OIDC <c>repository</c> claim with the owner prefix stripped.</summary>
    [Required(AllowEmptyStrings = false)]
    [MaxLength(128)]
    public required string Repo { get; init; }

    /// <summary>
    /// What this repository may publish: <c>publish_firmware</c>, <c>publish_modules</c>, or both.
    /// </summary>
    /// <remarks>
    /// Firmware and desktop ingestion share one authentication scheme, so an unscoped grant would let
    /// a repository onboarded for desktop modules publish firmware too. Omitting this registers the
    /// repository without letting it publish anything.
    /// </remarks>
    public List<string>? Scopes { get; init; }
}
