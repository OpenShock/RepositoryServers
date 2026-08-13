using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace OpenShock.RepositoryServer.Tests.Integration;

/// <summary>
/// Stands in for the GitHub session cookie on the <see cref="AuthSchemas.AdminCookie"/> scheme.
/// </summary>
/// <remarks>
/// The real scheme holds a cookie minted at the end of an OAuth round trip that a test cannot
/// perform: it would need to talk to GitHub. This handler substitutes only that half, producing the
/// same principal the login would have produced, so the authorization policy and every admin
/// endpoint behind it run for real against the claims production emits.
///
/// What it deliberately does not cover is the login itself: the team membership check, the rejection
/// of users outside the team, and the claim trimming all live in
/// <c>GitHubAuthentication.OnCreatingTicket</c>.
/// </remarks>
public sealed class TestAdminAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    /// <summary>Team slug the test host is configured to treat as admin.</summary>
    public const string AdminTeam = "repo-server-admins";

    /// <summary>Presence of this header is what makes a request authenticated at all.</summary>
    public const string UserHeader = "X-Test-Admin-User";

    /// <summary>
    /// Overrides the team claim, so a test can present a session that authenticated but does not
    /// satisfy the admin policy.
    /// </summary>
    public const string TeamHeader = "X-Test-Admin-Team";

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
        var team = Request.Headers.TryGetValue(TeamHeader, out var rawTeam) && !string.IsNullOrWhiteSpace(rawTeam)
            ? rawTeam.ToString()
            : AdminTeam;

        var claims = new List<Claim>
        {
            new(AuthSchemas.AdminClaims.Subject, username),
            new(AuthSchemas.AdminClaims.Username, username),
            new(AuthSchemas.AdminClaims.Team, team)
        };

        var identity = new ClaimsIdentity(claims, Scheme.Name,
            AuthSchemas.AdminClaims.Username, AuthSchemas.AdminClaims.Team);
        var principal = new ClaimsPrincipal(identity);

        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name)));
    }
}
