using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OpenShock.RepositoryServer.Models;
using OpenShock.RepositoryServer.Problems;
using OpenShock.RepositoryServer.RepoServerDb;
using Semver;
using Module = OpenShock.RepositoryServer.RepoServerDb.Module;
using Version = OpenShock.RepositoryServer.RepoServerDb.Version;

namespace OpenShock.RepositoryServer.Controllers.V1;

[ApiVersion("1.0")]
[ApiController]
[Route("/v{version:apiVersion}/admin")]
[Authorize(AuthenticationSchemes = AuthSchemas.AdminToken)]
public class AdminController : OpenShockControllerBase
{
    private readonly RepoServerContext _db;

    public AdminController(RepoServerContext db)
    {
        _db = db;
    }

    [HttpPut("modules/{moduleId}")]
    public async Task<IActionResult> CreateModule([FromBody] CreateModuleRequest createModuleRequest, [FromRoute] string moduleId)
    {
        var module = new Module
        {
            Id = moduleId.ToLowerInvariant(),
            Name = createModuleRequest.Name,
            Description = createModuleRequest.Description,
            SourceUrl = createModuleRequest.SourceUrl,
            IconUrl = createModuleRequest.IconUrl
        };
        var executed = await _db.Modules.Upsert(module).On(x => x.Id).RunAsync();

        if (executed <= 0) throw new Exception("Failed to upsert module");

        return Created();
    }

    [HttpDelete("modules/{moduleId}")]
    public async Task<IActionResult> DeleteModule([FromRoute] string moduleId)
    {
        var executed = await _db.Modules.Where(x => x.Id == moduleId.ToLowerInvariant()).ExecuteDeleteAsync();

        if(executed <= 0)  return Problem(ModuleError.ModuleNotFound);

        return Ok();
    }

    [Obsolete("Use the CI/CD endpoint at /v1/cicd/modules/{moduleId}/versions/{moduleVersion} instead.")]
    [HttpPut("modules/{moduleId}/versions/{moduleVersion}")]
    public async Task<IActionResult> CreateVersion([FromBody] CreateModuleVersionRequest createModuleVersionRequest, [FromRoute] string moduleId, [FromRoute] string moduleVersion)
    {
        moduleId = moduleId.ToLowerInvariant();
        moduleVersion = moduleVersion.ToLowerInvariant();

        if (!SemVersion.TryParse(moduleVersion, SemVersionStyles.Strict, out _))
        {
            return Problem(VersionError.VersionInvalidSemver);
        }

        if(!await _db.Modules.AnyAsync(x => x.Id == moduleId))
        {
            return Problem(ModuleError.ModuleNotFound);
        }

        var version = new Version
        {
            Module = moduleId,
            VersionName = moduleVersion,
            ZipUrl = createModuleVersionRequest.ZipUrl,
            HashSha256 = createModuleVersionRequest.Sha256Hash,
            ChangelogUrl = createModuleVersionRequest.ChangelogUrl,
            ReleaseUrl = createModuleVersionRequest.ReleaseUrl
        };

        var executed = await _db.Versions.Upsert(version).On(x => new { x.Module, x.VersionName }).RunAsync();

        if (executed <= 0) throw new Exception("Failed to upsert version");

        return Created();
    }

    [HttpDelete("modules/{moduleId}/versions/{moduleVersion}")]
    public async Task<IActionResult> DeleteVersion([FromRoute] string moduleId, [FromRoute] string moduleVersion)
    {
        var executed = await _db.Versions.Where(x => x.Module == moduleId && x.VersionName == moduleVersion).ExecuteDeleteAsync();
        if(executed <= 0)  return Problem(VersionError.VersionNotFound);
        return Ok();
    }
}
