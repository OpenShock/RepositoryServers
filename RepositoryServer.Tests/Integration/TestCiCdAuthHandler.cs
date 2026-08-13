using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace OpenShock.RepositoryServer.Tests.Integration;

/// <summary>
/// Stands in for the GitHub OIDC handler on the <see cref="AuthSchemas.CiCdToken"/> scheme.
/// </summary>
/// <remarks>
/// The real handler validates a GitHub-signed JWT against live JWKS, which a test cannot mint. It
/// then resolves the token's repository against the <c>repositories</c> allowlist and attaches
/// <see cref="AuthSchemas.CiCdClaims"/>. This handler substitutes only the token-validation half:
/// tests declare which registered repository is calling, and everything downstream — the ownership
/// checks, the state machine — runs for real against the same claims the production handler emits.
///
/// What this deliberately does <em>not</em> cover is the allowlist lookup itself. That path is
/// verified by construction instead: there is no code anywhere that inserts a repository row outside
/// the admin endpoint.
/// </remarks>
public sealed class TestCiCdAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string RepositoryIdHeader = "X-Test-Repository-Id";
    public const string CommitHashHeader = "X-Test-Commit-Hash";
    public const string RefHeader = "X-Test-Ref";
    public const string RunIdHeader = "X-Test-Run-Id";

    /// <summary>
    /// Comma-separated scopes. Defaults to both, so tests opt in to restricting a grant. Use
    /// <see cref="NoScopes"/> to model a repository that is registered but granted nothing — an
    /// empty header value cannot express that, because HttpClient drops empty headers.
    /// </summary>
    public const string ScopesHeader = "X-Test-Scopes";

    /// <summary>Sentinel meaning "registered, but no scopes granted".</summary>
    public const string NoScopes = "none";

    public TestCiCdAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(RepositoryIdHeader, out var rawRepositoryId))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        if (!Guid.TryParse(rawRepositoryId.ToString(), out var repositoryId))
        {
            return Task.FromResult(AuthenticateResult.Fail("Malformed test repository id."));
        }

        var commitHash = Request.Headers.TryGetValue(CommitHashHeader, out var rawCommit)
            ? rawCommit.ToString()
            : "abc1234567890abcdef1234567890abcdef12345";

        var claims = new List<Claim>
        {
            new(AuthSchemas.CiCdClaims.RepositoryId, repositoryId.ToString()),
            new(AuthSchemas.CiCdClaims.CommitHash, commitHash)
        };

        var rawScopes = Request.Headers.TryGetValue(ScopesHeader, out var scopeHeader)
            ? scopeHeader.ToString()
            : "publish_firmware,publish_modules";
        if (!string.Equals(rawScopes, NoScopes, StringComparison.Ordinal))
        {
            foreach (var scope in rawScopes.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                claims.Add(new Claim(AuthSchemas.CiCdClaims.Scope, scope));
            }
        }

        if (Request.Headers.TryGetValue(RefHeader, out var rawRef) && !string.IsNullOrWhiteSpace(rawRef))
        {
            claims.Add(new Claim(AuthSchemas.CiCdClaims.Ref, rawRef.ToString()));
        }

        if (Request.Headers.TryGetValue(RunIdHeader, out var rawRunId) && !string.IsNullOrWhiteSpace(rawRunId))
        {
            claims.Add(new Claim(AuthSchemas.CiCdClaims.RunId, rawRunId.ToString()));
        }

        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var principal = new ClaimsPrincipal(identity);

        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name)));
    }
}
