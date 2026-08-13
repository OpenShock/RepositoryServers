using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using OpenShock.RepositoryServer.Enums;
using OpenShock.RepositoryServer.RepoServerDb;
using OpenShock.RepositoryServer.Utils;
using System.Security.Claims;

namespace OpenShock.RepositoryServer.AuthenticationHandlers;

/// <summary>
/// Configures the <see cref="AuthSchemas.CiCdToken"/> scheme as a GitHub OIDC token
/// validator. The ASP.NET Core <see cref="JwtBearerHandler"/> does the JWT + JWKS
/// cryptography; this class adds an <see cref="JwtBearerEvents.OnTokenValidated"/>
/// hook that:
///   1. Extracts owner/repo/commit/ref/run_id from the token claims.
///   2. Looks up the <c>repositories</c> row for this owner/repo pair, failing if absent.
///   3. Attaches <see cref="AuthSchemas.CiCdClaims"/> to the principal so controllers
///      can pull the matched repository id, commit SHA, ref, and run id.
/// </summary>
/// <remarks>
/// A valid GitHub OIDC token proves only that <em>some</em> GitHub Actions workflow requested it. The
/// issuer is shared by every repository on GitHub, and the audience is a plain string that any workflow
/// can ask for by name, so neither authenticates <em>which</em> repository is calling. Registration in
/// the <c>repositories</c> table is therefore the actual authorization decision: an unregistered
/// repository is rejected here rather than being registered on the spot. Onboarding is an explicit
/// admin action — see <c>PUT /v2/firmware/admin/repositories</c>.
/// </remarks>
public static class GitHubOidcAuthentication
{
    public const string Issuer = "https://token.actions.githubusercontent.com";

    public static void Configure(JwtBearerOptions options, string audience)
    {
        options.Authority = Issuer;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = Issuer,
            ValidateAudience = true,
            ValidAudience = audience,
            ValidateLifetime = true
        };

        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = OnTokenValidatedAsync
        };
    }

    private static async Task OnTokenValidatedAsync(TokenValidatedContext context)
    {
        var principal = context.Principal;
        if (principal is null)
        {
            context.Fail("Token principal missing.");
            return;
        }

        var owner = principal.FindFirstValue("repository_owner");
        var repoFull = principal.FindFirstValue("repository"); // format: "owner/repo"
        var commitHash = principal.FindFirstValue("sha");
        var refValue = principal.FindFirstValue("ref");
        var runId = principal.FindFirstValue("run_id");

        if (string.IsNullOrWhiteSpace(owner) ||
            string.IsNullOrWhiteSpace(repoFull) ||
            string.IsNullOrWhiteSpace(commitHash))
        {
            context.Fail("Required GitHub OIDC claims missing.");
            return;
        }

        var repo = repoFull;
        var slashIndex = repoFull.IndexOf('/');
        if (slashIndex >= 0 && slashIndex < repoFull.Length - 1)
        {
            repo = repoFull[(slashIndex + 1)..];
        }

        var dbFactory = context.HttpContext.RequestServices
            .GetRequiredService<IDbContextFactory<RepoServerContext>>();
        await using var db = await dbFactory.CreateDbContextAsync(context.HttpContext.RequestAborted);

        var registration = await FindRepositoryAsync(db, owner, repo, context.HttpContext.RequestAborted);
        if (registration is null)
        {
            // Deliberately does not disclose whether the repository is merely unregistered.
            context.Fail("Repository is not authorized to publish.");
            return;
        }

        var identity = (ClaimsIdentity)principal.Identity!;
        identity.AddClaim(new Claim(AuthSchemas.CiCdClaims.RepositoryId, registration.Id.ToString()));

        // Scopes decide which ingestion endpoints this grant reaches. A repository with none is
        // registered but cannot publish anything.
        foreach (var scope in registration.Scopes)
        {
            identity.AddClaim(new Claim(AuthSchemas.CiCdClaims.Scope, scope.ToScopeClaim()));
        }
        identity.AddClaim(new Claim(AuthSchemas.CiCdClaims.CommitHash, commitHash));
        if (!string.IsNullOrWhiteSpace(refValue))
            identity.AddClaim(new Claim(AuthSchemas.CiCdClaims.Ref, refValue));
        if (!string.IsNullOrWhiteSpace(runId))
            identity.AddClaim(new Claim(AuthSchemas.CiCdClaims.RunId, runId));
    }

    private sealed record Registration(Guid Id, RepositoryScope[] Scopes);

    /// <summary>
    /// Resolves a pre-registered repository. Returns <c>null</c> when the pair is not registered —
    /// never creates the row, since that would make the allowlist self-populating and authorize
    /// whoever showed up first.
    /// </summary>
    /// <remarks>
    /// Owner and repo are matched case-insensitively. GitHub treats them that way, and the casing in
    /// the token's claims follows whatever the repository is currently named, so an exact match would
    /// silently stop authorizing a repository after a cosmetic rename.
    /// </remarks>
    private static async Task<Registration?> FindRepositoryAsync(RepoServerContext db, string owner, string repo, CancellationToken ct)
    {
        const RepositoryProvider provider = RepositoryProvider.Github;
        var loweredOwner = owner.ToLowerInvariant();
        var loweredRepo = repo.ToLowerInvariant();

        return await db.Repositories
            .Where(r => r.Provider == provider
                        && r.Owner.ToLower() == loweredOwner
                        && r.Repo.ToLower() == loweredRepo)
            .Select(r => new Registration(r.Id, r.Scopes))
            .FirstOrDefaultAsync(ct);
    }
}
