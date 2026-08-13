using System.ComponentModel.DataAnnotations;

namespace OpenShock.RepositoryServer.Config;

/// <summary>
/// Settings for admin authentication through Authentik. Admin endpoints have no other credential:
/// there is no static token to fall back on, so a misconfigured provider means no admin access.
/// </summary>
public sealed class AuthentikConfig
{
    /// <summary>
    /// OpenID Connect issuer for the Authentik application, e.g.
    /// <c>https://authentik.example.net/application/o/repository-server/</c>. Discovery is fetched
    /// from <c>{Authority}.well-known/openid-configuration</c>.
    /// </summary>
    /// <remarks>
    /// Must match the <c>iss</c> claim Authentik puts in its tokens exactly, trailing slash included,
    /// or every login fails token validation rather than failing at discovery, which is the more
    /// confusing of the two.
    /// </remarks>
    [Required(AllowEmptyStrings = false)]
    public required string Authority { get; init; }

    [Required(AllowEmptyStrings = false)]
    public required string ClientId { get; init; }

    /// <summary>
    /// Confidential client secret. The authorization code is exchanged server-side, so this never
    /// reaches a browser.
    /// </summary>
    [Required(AllowEmptyStrings = false)]
    public required string ClientSecret { get; init; }

    /// <summary>
    /// Group that grants admin access. A login by a user outside this group is rejected at the
    /// callback rather than being given a session that can do nothing.
    /// </summary>
    [Required(AllowEmptyStrings = false)]
    public required string AdminGroup { get; init; }

    /// <summary>
    /// Path Authentik redirects back to. Must be registered verbatim as a redirect URI on the
    /// provider, as an absolute URL on this server's public origin.
    /// </summary>
    public string CallbackPath { get; init; } = "/auth/callback";

    /// <summary>Path Authentik redirects back to after a front-channel logout.</summary>
    public string SignedOutCallbackPath { get; init; } = "/auth/signout-callback";

    /// <summary>How long an admin session lasts before a fresh login is required.</summary>
    public TimeSpan SessionLifetime { get; init; } = TimeSpan.FromHours(8);

    /// <summary>
    /// Allows discovery over plain HTTP. For local development against an Authentik instance without
    /// TLS only; leaving this on in production makes the discovery document trivially forgeable.
    /// </summary>
    public bool RequireHttpsMetadata { get; init; } = true;

    /// <summary>
    /// Directory to persist data protection keys to. Session cookies are encrypted with these, so
    /// without a shared path every replica issues cookies the others cannot read, and each restart
    /// invalidates every session. Leave null for single-instance deployments.
    /// </summary>
    public string? DataProtectionKeyPath { get; init; }
}
