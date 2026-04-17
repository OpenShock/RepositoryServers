using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using OpenShock.RepositoryServer.Enums;
using OpenShock.RepositoryServer.RepoServerDb;

namespace OpenShock.RepositoryServer.AuthenticationHandlers;

/// <summary>
/// Configures the <see cref="AuthSchemas.CiCdToken"/> scheme as a GitHub OIDC token
/// validator. The ASP.NET Core <see cref="JwtBearerHandler"/> does the JWT + JWKS
/// cryptography; this class adds an <see cref="JwtBearerEvents.OnTokenValidated"/>
/// hook that:
///   1. Extracts owner/repo/commit/ref/run_id from the token claims.
///   2. Looks up or auto-inserts the <c>repositories</c> row for this owner/repo pair.
///   3. Attaches <see cref="AuthSchemas.CiCdClaims"/> to the principal so controllers
///      can pull the matched repository id, commit SHA, ref, and run id.
/// </summary>
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

        var repositoryId = await GetOrCreateRepositoryAsync(db, owner, repo, context.HttpContext.RequestAborted);

        var identity = (ClaimsIdentity)principal.Identity!;
        identity.AddClaim(new Claim(AuthSchemas.CiCdClaims.RepositoryId, repositoryId.ToString()));
        identity.AddClaim(new Claim(AuthSchemas.CiCdClaims.CommitHash, commitHash));
        if (!string.IsNullOrWhiteSpace(refValue))
            identity.AddClaim(new Claim(AuthSchemas.CiCdClaims.Ref, refValue));
        if (!string.IsNullOrWhiteSpace(runId))
            identity.AddClaim(new Claim(AuthSchemas.CiCdClaims.RunId, runId));
    }

    private static async Task<Guid> GetOrCreateRepositoryAsync(RepoServerContext db, string owner, string repo, CancellationToken ct)
    {
        const RepositoryProvider provider = RepositoryProvider.Github;

        var existing = await db.Repositories
            .Where(r => r.Provider == provider && r.Owner == owner && r.Repo == repo)
            .Select(r => (Guid?)r.Id)
            .FirstOrDefaultAsync(ct);

        if (existing is not null)
        {
            return existing.Value;
        }

        var row = new SourceRepository
        {
            Id = Guid.NewGuid(),
            Provider = provider,
            Owner = owner,
            Repo = repo
        };

        db.Repositories.Add(row);

        try
        {
            await db.SaveChangesAsync(ct);
            return row.Id;
        }
        catch (DbUpdateException)
        {
            // Race: another concurrent token validation inserted the same (provider, owner, repo).
            // Re-read and return.
            db.Entry(row).State = EntityState.Detached;

            var raced = await db.Repositories
                .Where(r => r.Provider == provider && r.Owner == owner && r.Repo == repo)
                .Select(r => (Guid?)r.Id)
                .FirstOrDefaultAsync(ct);

            if (raced is null)
            {
                throw;
            }

            return raced.Value;
        }
    }
}
