using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OpenShock.RepositoryServer.Models.Firmware;
using OpenShock.RepositoryServer.Problems;
using OpenShock.RepositoryServer.RepoServerDb;

namespace OpenShock.RepositoryServer.Controllers.V2.Firmware.Admin;

/// <summary>
/// Read-only + destructive admin for the shared <c>repositories</c> table. Rows are
/// auto-created by <see cref="OpenShock.RepositoryServer.AuthenticationHandlers.GitHubOidcAuthentication"/>
/// on the first CI/CD request from a given owner/repo pair — there is no create/upsert
/// endpoint here by design.
/// </summary>
[ApiVersion("2.0")]
[ApiController]
[Route("/v{version:apiVersion}/firmware/admin/repositories")]
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
