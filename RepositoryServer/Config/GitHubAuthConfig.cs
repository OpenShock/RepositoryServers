using System.ComponentModel.DataAnnotations;

namespace OpenShock.RepositoryServer.Config;

/// <summary>
/// Settings for admin authentication through GitHub. Admin endpoints have no other credential:
/// there is no static token to fall back on, so a misconfigured app means no admin access.
/// </summary>
public sealed class GitHubAuthConfig
{
    /// <summary>Client id of the GitHub OAuth app.</summary>
    [Required(AllowEmptyStrings = false)]
    public required string ClientId { get; init; }

    /// <summary>
    /// Client secret. The authorization code is exchanged server-side, so this never reaches a
    /// browser.
    /// </summary>
    [Required(AllowEmptyStrings = false)]
    public required string ClientSecret { get; init; }

    /// <summary>
    /// Organization owning the admin team, e.g. <c>OpenShock</c>. Matched case-insensitively, the
    /// way GitHub treats org names.
    /// </summary>
    [Required(AllowEmptyStrings = false)]
    public required string Organization { get; init; }

    /// <summary>
    /// Team <em>slug</em> whose members are administrators, e.g. <c>maintainers</c>. This is the URL
    /// form, not the display name: a team shown as "Repo Maintainers" has the slug
    /// <c>repo-maintainers</c>, and the display name will not resolve.
    /// </summary>
    /// <remarks>
    /// Membership is checked once, at login, against
    /// <c>/orgs/{org}/teams/{team}/memberships/{user}</c>, and only an <c>active</c> membership
    /// counts — a pending invitation does not.
    /// </remarks>
    [Required(AllowEmptyStrings = false)]
    public required string Team { get; init; }

    /// <summary>
    /// Path GitHub redirects back to. Must match the authorization callback URL registered on the
    /// OAuth app, as an absolute URL on this server's public origin.
    /// </summary>
    public string CallbackPath { get; init; } = "/auth/callback";

    /// <summary>How long an admin session lasts before a fresh login is required.</summary>
    public TimeSpan SessionLifetime { get; init; } = TimeSpan.FromHours(8);
}
