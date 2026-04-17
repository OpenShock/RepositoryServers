using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OpenShock.RepositoryServer.Problems;
using OpenShock.RepositoryServer.RepoServerDb;

namespace OpenShock.RepositoryServer.Controllers.V2.Firmware.Admin;

[ApiVersion("2.0")]
[ApiController]
[Route("/v{version:apiVersion}/firmware/admin/versions")]
[Authorize(AuthenticationSchemes = AuthSchemas.AdminToken)]
public class VersionsAdminController : OpenShockControllerBase
{
    private readonly RepoServerContext _db;

    public VersionsAdminController(RepoServerContext db)
    {
        _db = db;
    }

    [HttpDelete("{firmwareVersion}")]
    public async Task<IActionResult> DeleteVersion([FromRoute] string firmwareVersion, CancellationToken ct)
    {
        var deleted = await _db.FirmwareVersions
            .Where(v => v.Version == firmwareVersion)
            .ExecuteDeleteAsync(ct);

        if (deleted <= 0)
        {
            return Problem(FirmwareError.FirmwareVersionNotFound);
        }

        return NoContent();
    }
}
