using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace OpenShock.RepositoryServer.Tests.Integration;

/// <summary>
/// Stands in for the Authentik session cookie on the <see cref="AuthSchemas.AdminCookie"/> scheme.
/// </summary>
/// <remarks>
/// The real scheme holds a cookie minted at the end of an OpenID Connect round trip that a test
/// cannot perform: it would need a live Authentik to sign the id token. This handler substitutes only
/// that half, producing the same principal the login would have produced, so the authorization policy
/// and every admin endpoint behind it run for real against the claims production emits.
///
/// What it deliberately does not cover is the login itself: group extraction, the rejection of users
/// outside the admin group, and the claim trimming all live in
/// <c>AuthentikAuthentication.OnTicketReceived</c> and are exercised by unit tests instead.
/// </remarks>
public sealed class TestAdminAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    /// <summary>Group the test host is configured to treat as admin.</summary>
    public const string AdminGroup = "repo-server-admins";

    /// <summary>Presence of this header is what makes a request authenticated at all.</summary>
    public const string UserHeader = "X-Test-Admin-User";

    /// <summary>
    /// Overrides the group claim, so a test can present a session that authenticated but does not
    /// satisfy the admin policy.
    /// </summary>
    public const string GroupHeader = "X-Test-Admin-Group";

    public TestAdminAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(UserHeader, out var rawUser) || string.IsNullOrWhiteSpace(rawUser))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var username = rawUser.ToString();
        var group = Request.Headers.TryGetValue(GroupHeader, out var rawGroup) && !string.IsNullOrWhiteSpace(rawGroup)
            ? rawGroup.ToString()
            : AdminGroup;

        var claims = new List<Claim>
        {
            new(AuthSchemas.AdminClaims.Subject, username),
            new(AuthSchemas.AdminClaims.Username, username),
            new(AuthSchemas.AdminClaims.Group, group)
        };

        var identity = new ClaimsIdentity(claims, Scheme.Name,
            AuthSchemas.AdminClaims.Username, AuthSchemas.AdminClaims.Group);
        var principal = new ClaimsPrincipal(identity);

        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name)));
    }
}
