using System.IO.Compression;
using System.Security.Cryptography;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OpenShock.RepositoryServer.Config;
using OpenShock.RepositoryServer.Problems;
using OpenShock.RepositoryServer.RepoServerDb;
using OpenShock.RepositoryServer.Services;
using Semver;
using Version = OpenShock.RepositoryServer.RepoServerDb.Version;

namespace OpenShock.RepositoryServer.Controllers.V1;

[ApiVersion("1.0")]
[ApiController]
[Route("/v{version:apiVersion}/cicd")]
[Authorize(AuthenticationSchemes = AuthSchemas.CiCdToken)]
public class CiCdController : OpenShockControllerBase
{
    private readonly RepoServerContext _db;
    private readonly IStorageService _storage;
    private readonly ApiConfig _apiConfig;

    public CiCdController(RepoServerContext db, IStorageService storage, ApiConfig apiConfig)
    {
        _db = db;
        _storage = storage;
        _apiConfig = apiConfig;
    }

    /// <summary>
    /// Publishes a desktop module version by uploading a zip file.
    /// The file is hashed server-side, uploaded to storage, and recorded in the database.
    /// </summary>
    [HttpPut("modules/{moduleId}/versions/{moduleVersion}")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(256 * 1024 * 1024)] // 256 MB
    public async Task<IActionResult> PublishVersion(
        [FromRoute] string moduleId,
        [FromRoute] string moduleVersion,
        [FromQuery] string? changelogUrl,
        [FromQuery] string? releaseUrl)
    {
        moduleId = moduleId.ToLowerInvariant();
        moduleVersion = moduleVersion.ToLowerInvariant();

        if (!SemVersion.TryParse(moduleVersion, SemVersionStyles.Strict, out _))
        {
            return Problem(VersionError.VersionInvalidSemver);
        }

        if (!await _db.Modules.AnyAsync(x => x.Id == moduleId))
        {
            return Problem(ModuleError.ModuleNotFound);
        }

        var file = Request.Form.Files.GetFile("zip");
        if (file == null)
        {
            return BadRequest(new { error = "No file uploaded. Expected a file field named 'zip'." });
        }

        // Read file into memory for validation + hashing + storage upload
        using var memoryStream = new MemoryStream();
        await file.CopyToAsync(memoryStream);
        var fileBytes = memoryStream.ToArray();

        // Validate zip contents
        memoryStream.Position = 0;
        try
        {
            using var zip = new ZipArchive(memoryStream, ZipArchiveMode.Read, leaveOpen: true);

            if (zip.Entries.Count == 0)
                return Problem(ModuleError.ZipEmpty);

            foreach (var entry in zip.Entries)
            {
                if (entry.FullName.Contains("..") ||
                    Path.IsPathRooted(entry.FullName))
                    return Problem(ModuleError.ZipPathTraversal);

                // Determine depth and root segment
                var normalized = entry.FullName.Replace('\\', '/');
                var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);

                if (segments.Length == 0)
                    continue;

                // Directory entries at root: only "wwwroot/" is allowed
                if (segments.Length >= 2 && !segments[0].Equals("wwwroot", StringComparison.OrdinalIgnoreCase))
                    return Problem(ModuleError.ZipDisallowedDirectory(segments[0]));

                // Files at root: only .dll, .pdb, .json
                if (segments.Length == 1 && !entry.FullName.EndsWith('/'))
                {
                    var ext = Path.GetExtension(entry.Name).ToLowerInvariant();
                    if (ext is not (".dll" or ".pdb" or ".json"))
                        return Problem(ModuleError.ZipDisallowedRootFile(entry.Name));
                }
            }
        }
        catch (InvalidDataException)
        {
            return Problem(ModuleError.ZipInvalid);
        }

        // Compute SHA256 hash server-side
        var hashBytes = SHA256.HashData(fileBytes);

        // Upload to storage
        var storagePath = $"modules/{moduleId}/{moduleVersion}/module.zip";
        using var uploadStream = new MemoryStream(fileBytes);
        await _storage.UploadFileAsync(storagePath, uploadStream);

        // Construct public download URL
        var cdnBase = _apiConfig.Repo.CdnBaseUrl.TrimEnd('/');
        var zipUrl = new Uri($"{cdnBase}/{storagePath}");

        var version = new Version
        {
            Module = moduleId,
            VersionName = moduleVersion,
            ZipUrl = zipUrl,
            HashSha256 = hashBytes,
            ChangelogUrl = changelogUrl != null ? new Uri(changelogUrl) : null,
            ReleaseUrl = releaseUrl != null ? new Uri(releaseUrl) : null
        };

        var executed = await _db.Versions.Upsert(version).On(x => new { x.Module, x.VersionName }).RunAsync();
        if (executed <= 0) throw new Exception("Failed to upsert version");

        return Created();
    }
}
