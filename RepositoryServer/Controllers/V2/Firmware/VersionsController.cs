using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OpenShock.RepositoryServer.Config;
using OpenShock.RepositoryServer.Enums;
using OpenShock.RepositoryServer.Models.Firmware;
using OpenShock.RepositoryServer.Problems;
using OpenShock.RepositoryServer.RepoServerDb;
using OpenShock.RepositoryServer.Utils;

namespace OpenShock.RepositoryServer.Controllers.V2.Firmware;

[ApiVersion("2.0")]
[ApiController]
[Route("/v{version:apiVersion}/firmware/versions")]
public sealed class VersionsController : OpenShockControllerBase
{
    private readonly RepoServerContext _db;
    private readonly ApiConfig _apiConfig;

    public VersionsController(RepoServerContext db, ApiConfig apiConfig)
    {
        _db = db;
        _apiConfig = apiConfig;
    }

    [HttpGet]
    public async Task<IActionResult> ListVersions([FromQuery] string? channel)
    {
        IQueryable<FirmwareVersion> query = _db.FirmwareVersions;

        if (!string.IsNullOrWhiteSpace(channel))
        {
            if (!Enum.TryParse<ReleaseChannel>(channel, true, out var firmwareChannel))
            {
                return Problem(FirmwareError.FirmwareInvalidChannel);
            }
            query = query.Where(v => v.Channel == firmwareChannel);
        }

        var versions = await query
            .OrderByDescending(v => v.ReleaseDate)
            .Select(v => new FirmwareVersionSummary
            {
                Version = v.Version,
                Channel = v.Channel.ToString().ToLowerInvariant(),
                ReleaseDate = v.ReleaseDate
            })
            .ToListAsync();

        return Ok(versions);
    }

    [HttpGet("{firmwareVersion}")]
    public async Task<IActionResult> GetVersion([FromRoute] string firmwareVersion)
    {
        var version = await _db.FirmwareVersions
            .Include(v => v.Artifacts)
            .Include(v => v.ReleaseNotes.OrderBy(n => n.Index))
            .FirstOrDefaultAsync(v => v.Version == firmwareVersion);

        if (version == null)
        {
            return Problem(FirmwareError.FirmwareVersionNotFound);
        }

        var cdnBase = _apiConfig.Firmware.CdnBaseUrl.TrimEnd('/');

        var artifacts = new Dictionary<string, List<FirmwareArtifactDto>>();
        foreach (var artifact in version.Artifacts)
        {
            if (!artifacts.TryGetValue(artifact.BoardId, out var list))
            {
                list = new List<FirmwareArtifactDto>();
                artifacts[artifact.BoardId] = list;
            }

            list.Add(new FirmwareArtifactDto
            {
                Type = artifact.ArtifactType.ToString().ToLowerInvariant(),
                Url = $"{cdnBase}/{version.Version}/{artifact.BoardId}/{FirmwareArtifactFileNames.GetFileName(artifact.ArtifactType)}",
                Sha256Hash = Convert.ToHexString(artifact.HashSha256),
                FileSize = artifact.FileSize
            });
        }

        var releaseNotes = version.ReleaseNotes.Select(n => new FirmwareReleaseNoteDto
        {
            Type = n.SectionType.ToString().ToLowerInvariant(),
            Title = n.Title,
            Content = n.Content
        }).ToList();

        return Ok(new FirmwareVersionResponse
        {
            Version = version.Version,
            Channel = version.Channel.ToString().ToLowerInvariant(),
            ReleaseDate = version.ReleaseDate,
            CommitHash = version.CommitHash,
            ReleaseUrl = version.ReleaseUrl,
            Artifacts = artifacts,
            ReleaseNotes = releaseNotes
        });
    }

}
