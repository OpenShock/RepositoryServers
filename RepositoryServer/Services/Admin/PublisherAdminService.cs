using Microsoft.EntityFrameworkCore;
using OneOf.Types;
using OneOf;
using OpenShock.RepositoryServer.Enums;
using OpenShock.RepositoryServer.RepoServerDb.Models;
using OpenShock.RepositoryServer.RepoServerDb;
using OpenShock.RepositoryServer.Utils;

namespace OpenShock.RepositoryServer.Services.Admin;

/// <summary>
/// The <c>repositories</c> table, which doubles as the publish allowlist.
/// </summary>
/// <remarks>
/// Registering a repository here is what authorizes it to publish. Validating an OIDC token proves a
/// workflow ran somewhere on GitHub, not that it ran somewhere we trust, so onboarding is deliberately
/// a manual act and removing a row is a real revocation.
/// </remarks>
public sealed class PublisherAdminService
{
    private readonly RepoServerContext _db;

    public PublisherAdminService(RepoServerContext db)
    {
        _db = db;
    }

    public Task<SourceRepository[]> ListAsync(CancellationToken ct = default) =>
        _db.Repositories
            .OrderBy(r => r.Provider)
            .ThenBy(r => r.Owner)
            .ThenBy(r => r.Repo)
            .ToArrayAsync(ct);

    public Task<SourceRepository?> FindAsync(Guid id, CancellationToken ct = default) =>
        _db.Repositories.FirstOrDefaultAsync(r => r.Id == id, ct);

    /// <summary>
    /// Registers a repository, or updates the scopes of one already registered. Idempotent on
    /// identity, so re-running onboarding is harmless; scopes are authoritative, so this is also how a
    /// grant is widened or narrowed.
    /// </summary>
    public async Task<SourceRepository> UpsertAsync(
        RepositoryProvider provider, string owner, string repo, RepositoryScope[] scopes,
        CancellationToken ct = default)
    {
        // Matched case-insensitively, consistent with how GitHub treats owner and repo names and with
        // how the OIDC handler resolves them. An exact match would let "OpenShock/Firmware" create a
        // second row that a token for "openshock/firmware" would never reach.
        var loweredOwner = owner.ToLowerInvariant();
        var loweredRepo = repo.ToLowerInvariant();

        var distinctScopes = scopes.Distinct().ToArray();

        var existing = await _db.Repositories.FirstOrDefaultAsync(
            r => r.Provider == provider
                 && r.Owner.ToLower() == loweredOwner
                 && r.Repo.ToLower() == loweredRepo, ct);

        if (existing is not null)
        {
            existing.Scopes = distinctScopes;
            await _db.SaveChangesAsync(ct);
            return existing;
        }

        var row = new SourceRepository
        {
            Id = Guid.NewGuid(),
            Provider = provider,
            Owner = owner,
            Repo = repo,
            Scopes = distinctScopes
        };

        _db.Repositories.Add(row);
        await _db.SaveChangesAsync(ct);

        return row;
    }

    public async Task<OneOf<Success, NotFound, InUse>> DeleteAsync(Guid repositoryId, CancellationToken ct = default)
    {
        // Published versions and in-flight releases record which repository produced them. Removing
        // the row would erase that provenance, so revocation of a repository that has published
        // anything means removing its scopes instead.
        var inUse =
            await _db.FirmwareVersions.AnyAsync(v => v.RepositoryId == repositoryId, ct) ||
            await _db.FirmwareReleases.AnyAsync(r => r.RepositoryId == repositoryId, ct);

        if (inUse)
        {
            return new InUse("published versions or releases");
        }

        var deleted = await _db.Repositories.Where(r => r.Id == repositoryId).ExecuteDeleteAsync(ct);
        if (deleted <= 0)
        {
            return new NotFound();
        }

        return new Success();
    }
}
