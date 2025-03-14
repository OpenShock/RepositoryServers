using System.Collections.Immutable;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OpenShock.Desktop.RepositoryServer.Config;
using OpenShock.Desktop.RepositoryServer.Models;
using OpenShock.Desktop.RepositoryServer.RepoServerDb;
using OpenShock.Desktop.RepositoryServer.Utils;
using Semver;
using Module = OpenShock.Desktop.RepositoryServer.Models.Module;
using Version = OpenShock.Desktop.RepositoryServer.Models.Version;

namespace OpenShock.Desktop.RepositoryServer.Controllers;

[ApiController]
[Route("/{version:apiVersion}/")]
public sealed class RepoController : OpenShockControllerBase
{
    private readonly RepoServerContext _db;
    private readonly ApiConfig _apiConfig;

    public RepoController(RepoServerContext db, ApiConfig apiConfig)
    {
        _db = db;
        _apiConfig = apiConfig;
    }
    
    public async Task<IActionResult> GetRepo()
    {
        var moduleRaw = await _db.Modules.Include(x => x.Versions).ToArrayAsync();
        
        var modules = moduleRaw.ToImmutableDictionary(x => x.Id, x => new Module()
        {
            Name = x.Name,
            Description = x.Description,
            IconUrl = x.IconUrl,
            SourceUrl = x.SourceUrl,
            Versions = x.Versions.ToImmutableDictionary(
                y => SemVersion.Parse(y.VersionName, SemVersionStyles.Strict, 64), y => new Version
                {
                    Url = y.ZipUrl,
                    Sha256Hash = y.HashSha256,
                    ChangelogUrl = y.ChangelogUrl,
                    ReleaseUrl = y.ReleaseUrl
                })
        });

        var repository = new Repository
        {
            Id = _apiConfig.Repo.Id,
            Name = _apiConfig.Repo.Name,
            Author = _apiConfig.Repo.Author,
            Homepage = _apiConfig.Repo.Homepage,
            Modules = modules
        };
        
        return Ok(repository);
    }
}