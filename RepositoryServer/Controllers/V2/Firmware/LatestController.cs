using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OpenShock.RepositoryServer.Config;
using OpenShock.RepositoryServer.Models.Firmware;
using OpenShock.RepositoryServer.Problems;
using OpenShock.RepositoryServer.RepoServerDb;

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
    public async Task<IActionResult> GetLatest([FromRoute] string channel)
    {
        if (!Enum.TryParse<FirmwareChannel>(channel, true, out var firmwareChannel))
        {
            return Problem(FirmwareError.FirmwareInvalidChannel);
        }

        var latestVersion = await _db.FirmwareVersions
            .Where(v => v.Channel == firmwareChannel)
            .OrderByDescending(v => v.ReleaseDate)
            .Include(v => v.Artifacts.Where(a => a.ArtifactType == FirmwareArtifactType.Merged))
            .FirstOrDefaultAsync();

        if (latestVersion == null)
        {
            return Problem(FirmwareError.FirmwareVersionNotFound);
        }

        var cdnBase = _apiConfig.Firmware.CdnBaseUrl.TrimEnd('/');
        var artifacts = new Dictionary<string, FirmwareBoardArtifact>();

        foreach (var artifact in latestVersion.Artifacts)
        {
            artifacts[artifact.BoardId] = new FirmwareBoardArtifact
            {
                Url = $"{cdnBase}/{latestVersion.Version}/{artifact.BoardId}/{GetArtifactFileName(artifact.ArtifactType)}",
                Sha256Hash = Convert.ToHexString(artifact.HashSha256),
                FileSize = artifact.FileSize
            };
        }

        return Ok(new FirmwareLatestResponse
        {
            Version = latestVersion.Version,
            Channel = latestVersion.Channel.ToString().ToLowerInvariant(),
            ReleaseDate = latestVersion.ReleaseDate,
            Artifacts = artifacts
        });
    }

    private static string GetArtifactFileName(FirmwareArtifactType type) => type switch
    {
        FirmwareArtifactType.Merged => "firmware.bin",
        FirmwareArtifactType.App => "app.bin",
        FirmwareArtifactType.Bootloader => "bootloader.bin",
        FirmwareArtifactType.Partitions => "partitions.bin",
        FirmwareArtifactType.StaticFs => "staticfs.bin",
        _ => $"{type.ToString().ToLowerInvariant()}.bin"
    };
}
