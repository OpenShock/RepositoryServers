#if DEBUG
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using OpenShock.RepositoryServer.Config;

namespace OpenShock.RepositoryServer.AuthenticationHandlers;

/// <summary>
/// Authenticates every request as an administrator, replacing the GitHub session for local
/// development.
/// </summary>
/// <remarks>
/// This is an authentication bypass, so it is compiled only into Debug builds and registered only
/// when the environment is Development and the flag is set. The published image is a Release build
/// and does not contain this type.
///
/// It stands in for the cookie scheme rather than adding a new one, so the authorization policy,
/// the endpoints and the claims they read are the same ones production uses. Only the step that
/// proves who you are is skipped.
/// </remarks>
public sealed class DevAdminAuthentication : AuthenticationHandler<AuthenticationSchemeOptions>
{
    private readonly ApiConfig _apiConfig;

    public DevAdminAuthentication(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        ApiConfig apiConfig)
        : base(options, logger, encoder)
    {
        _apiConfig = apiConfig;
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var username = _apiConfig.DevAuth.Username;
        var team = _apiConfig.GitHub?.Team ?? AdminAuthMode.DevFallbackTeam;

        var identity = new ClaimsIdentity(
            [
                new Claim(AuthSchemas.AdminClaims.Subject, username),
                new Claim(AuthSchemas.AdminClaims.Username, username),
                new Claim(AuthSchemas.AdminClaims.Team, team)
            ],
            Scheme.Name,
            AuthSchemas.AdminClaims.Username,
            AuthSchemas.AdminClaims.Team);

        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
#endif
