using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OpenShock.RepositoryServer.Enums;
using OpenShock.RepositoryServer.Models.Firmware;
using OpenShock.RepositoryServer.Problems;
using OpenShock.RepositoryServer.RepoServerDb;
using OpenShock.RepositoryServer.Utils;

namespace OpenShock.RepositoryServer.Controllers.V2.Firmware.Admin;

/// <summary>
/// Admin for the shared <c>repositories</c> table, which doubles as the publish allowlist.
/// </summary>
/// <remarks>
/// Registration here is what authorizes a repository to publish: OIDC token validation proves a
/// workflow ran somewhere on GitHub, not that it ran in a repository we trust. Onboarding is therefore
/// deliberately a manual admin action, and deletion is a real revocation.
/// </remarks>
[ApiVersion("2.0")]
[ApiController]
[Route("/{version:apiVersion}/firmware/admin/repositories")]
[Authorize(AuthenticationSchemes = AuthSchemas.AdminToken)]
public class RepositoriesController : OpenShockControllerBase
{
    private readonly RepoServerContext _db;

    public RepositoriesController(RepoServerContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<IActionResult> ListRepositories(CancellationToken ct)
    {
        var rows = await _db.Repositories
            .OrderBy(r => r.Provider)
            .ThenBy(r => r.Owner)
            .ThenBy(r => r.Repo)
            .ToListAsync(ct);

        return Ok(rows.Select(RepositoryDto.From));
    }

    /// <summary>
    /// Registers a repository as authorized to publish, or returns the existing row if already
    /// registered. Idempotent, so re-running onboarding is harmless.
    /// </summary>
    [HttpPut]
    public async Task<IActionResult> UpsertRepository(
        [FromBody] UpsertRepositoryRequest request,
        CancellationToken ct)
    {
        if (!Enum.TryParse<RepositoryProvider>(request.Provider, true, out var provider))
        {
            return Problem(FirmwareError.FirmwareInvalidRepositoryProvider);
        }

        var scopes = new List<RepositoryScope>();
        foreach (var raw in request.Scopes ?? [])
        {
            if (!RepositoryScopeExtensions.TryParseScope(raw, out var scope))
            {
                return Problem(FirmwareError.FirmwareInvalidRepositoryScope(raw));
            }
            if (!scopes.Contains(scope)) scopes.Add(scope);
        }

        // Matched case-insensitively, consistent with how GitHub treats owner and repo names and with
        // how the OIDC handler resolves them — otherwise onboarding "OpenShock/Firmware" would create
        // a second row that the handler's lookup for "openshock/firmware" would never reach.
        var loweredOwner = request.Owner.ToLowerInvariant();
        var loweredRepo = request.Repo.ToLowerInvariant();

        var existing = await _db.Repositories.FirstOrDefaultAsync(
            r => r.Provider == provider
                 && r.Owner.ToLower() == loweredOwner
                 && r.Repo.ToLower() == loweredRepo, ct);

        if (existing is not null)
        {
            // Idempotent on identity, but scopes are authoritative: re-running onboarding with a
            // different set is how a grant is widened or narrowed.
            existing.Scopes = scopes.ToArray();
            await _db.SaveChangesAsync(ct);
            return Ok(RepositoryDto.From(existing));
        }

        var row = new SourceRepository
        {
            Id = Guid.NewGuid(),
            Provider = provider,
            Owner = request.Owner,
            Repo = request.Repo,
            Scopes = scopes.ToArray()
        };

        _db.Repositories.Add(row);
        await _db.SaveChangesAsync(ct);

        return Created((string?)null, RepositoryDto.From(row));
    }

    [HttpDelete("{repositoryId:guid}")]
    public async Task<IActionResult> DeleteRepository([FromRoute] Guid repositoryId, CancellationToken ct)
    {
        var inUse =
            await _db.FirmwareVersions.AnyAsync(v => v.RepositoryId == repositoryId, ct) ||
            await _db.FirmwareReleases.AnyAsync(r => r.RepositoryId == repositoryId, ct);

        if (inUse)
        {
            return Problem(FirmwareError.FirmwareRepositoryInUse);
        }

        var deleted = await _db.Repositories.Where(r => r.Id == repositoryId).ExecuteDeleteAsync(ct);
        if (deleted <= 0)
        {
            return Problem(FirmwareError.FirmwareRepositoryNotFound);
        }

        return NoContent();
    }
}
