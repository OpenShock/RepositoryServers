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
[Route("/v{version:apiVersion}/firmware/latest")]
public sealed class LatestController : OpenShockControllerBase
{
    private readonly RepoServerContext _db;
    private readonly ApiConfig _apiConfig;

    public LatestController(RepoServerContext db, ApiConfig apiConfig)
    {
        _db = db;
        _apiConfig = apiConfig;
    }

    [HttpGet("{channel}")]
    public async Task<IActionResult> GetLatest([FromRoute] string channel, [FromQuery] string? board)
    {
        if (!Enum.TryParse<FirmwareChannel>(channel, true, out var firmwareChannel))
        {
            return Problem(FirmwareError.FirmwareInvalidChannel);
        }

        var query = _db.FirmwareVersions
            .Where(v => v.Channel == firmwareChannel)
            .OrderByDescending(v => v.ReleaseDate);

        FirmwareVersion? latestVersion;
        if (!string.IsNullOrWhiteSpace(board))
        {
            latestVersion = await query
                .Include(v => v.Artifacts.Where(a => a.BoardId == board))
                .FirstOrDefaultAsync();
        }
        else
        {
            latestVersion = await query
                .Include(v => v.Artifacts)
                .FirstOrDefaultAsync();
        }

        if (latestVersion == null)
        {
            return Problem(FirmwareError.FirmwareVersionNotFound);
        }

        var cdnBase = _apiConfig.Firmware.CdnBaseUrl.TrimEnd('/');
        var artifacts = new Dictionary<string, List<FirmwareArtifactDto>>();

        foreach (var artifact in latestVersion.Artifacts)
        {
            if (!artifacts.TryGetValue(artifact.BoardId, out var list))
            {
                list = new List<FirmwareArtifactDto>();
                artifacts[artifact.BoardId] = list;
            }

            list.Add(new FirmwareArtifactDto
            {
                Type = artifact.ArtifactType.ToString().ToLowerInvariant(),
                Url = $"{cdnBase}/{latestVersion.Version}/{artifact.BoardId}/{FirmwareArtifactFileNames.GetFileName(artifact.ArtifactType)}",
                Sha256Hash = Convert.ToHexString(artifact.HashSha256),
                FileSize = artifact.FileSize
            });
        }

        return Ok(new FirmwareLatestResponse
        {
            Version = latestVersion.Version,
            Channel = latestVersion.Channel.ToString().ToLowerInvariant(),
            ReleaseDate = latestVersion.ReleaseDate,
            Artifacts = artifacts
        });
    }
}
