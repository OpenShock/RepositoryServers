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
    [CacheControl(300)]
    public async Task<IActionResult> GetLatest([FromRoute] string channel, CancellationToken ct)
    {
        if (!Enum.TryParse<ReleaseChannel>(channel, true, out var firmwareChannel))
        {
            return Problem(FirmwareError.FirmwareInvalidChannel);
        }

        var latest = await _db.FirmwareVersions
            .Where(v => v.Channel == firmwareChannel)
            .OrderByDescending(v => v.ReleaseDate)
            .Include(v => v.RepositoryNavigation)
            .Include(v => v.Artifacts)
            .Include(v => v.ReleaseNotes)
            .FirstOrDefaultAsync(ct);

        if (latest is null)
        {
            return Problem(FirmwareError.FirmwareVersionNotFound);
        }

        var boardIds = latest.Artifacts.Select(a => a.BoardId).Distinct().ToList();
        var boards = await _db.FirmwareBoards
            .Include(b => b.ChipNavigation)
            .Where(b => boardIds.Contains(b.Id))
            .ToListAsync(ct);

        var cdnBase = _apiConfig.Firmware.CdnBaseUrl.TrimEnd('/');
        return Ok(FirmwareResponseMapper.ToReleaseDto(latest, boards, cdnBase));
    }

    [HttpGet("{channel}/{boardId}")]
    [CacheControl(300)]
    public async Task<IActionResult> GetLatestForBoard(
        [FromRoute] string channel,
        [FromRoute] string boardId,
        [FromQuery] string? version,
        CancellationToken ct)
    {
        if (!Enum.TryParse<ReleaseChannel>(channel, true, out var firmwareChannel))
        {
            return Problem(FirmwareError.FirmwareInvalidChannel);
        }

        var latestVersion = await _db.FirmwareVersions
            .Where(v => v.Channel == firmwareChannel)
            .OrderByDescending(v => v.ReleaseDate)
            .Select(v => v.Version)
            .FirstOrDefaultAsync(ct);

        if (latestVersion is null)
        {
            return Problem(FirmwareError.FirmwareVersionNotFound);
        }

        // String equality (not semver) is intentional: rollbacks — if a version is pulled
        // and an older version becomes "latest", the hub's string compare will still differ
        // and trigger an update. See firmware-api-spec.md §4.2.
        if (!string.IsNullOrWhiteSpace(version) && string.Equals(version, latestVersion, StringComparison.Ordinal))
        {
            return NoContent();
        }

        var artifacts = await _db.FirmwareArtifacts
            .Where(a => a.Version == latestVersion && a.BoardId == boardId)
            .ToListAsync(ct);

        if (artifacts.Count == 0)
        {
            return Problem(FirmwareError.FirmwareBoardNotFound);
        }

        var cdnBase = _apiConfig.Firmware.CdnBaseUrl.TrimEnd('/');
        return Ok(new FirmwareBoardReleaseResponseDto
        {
            Version = latestVersion,
            BoardId = boardId,
            Artifacts = artifacts
                .Select(a => FirmwareResponseMapper.ToArtifactDto(a, latestVersion, cdnBase))
                .ToList()
        });
    }
}
