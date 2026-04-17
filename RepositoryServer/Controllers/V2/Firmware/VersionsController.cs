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
    private const int DefaultLimit = 20;
    private const int MaxLimit = 100;

    private readonly RepoServerContext _db;
    private readonly ApiConfig _apiConfig;

    public VersionsController(RepoServerContext db, ApiConfig apiConfig)
    {
        _db = db;
        _apiConfig = apiConfig;
    }

    [HttpGet]
    [CacheControl(3600)]
    public async Task<IActionResult> ListVersions(
        [FromQuery] string? channel,
        [FromQuery] int? limit,
        [FromQuery] int? offset,
        CancellationToken ct)
    {
        IQueryable<FirmwareVersion> query = _db.FirmwareVersions
            .Include(v => v.RepositoryNavigation)
            .Include(v => v.ReleaseNotes);

        if (!string.IsNullOrWhiteSpace(channel))
        {
            if (!Enum.TryParse<ReleaseChannel>(channel, true, out var firmwareChannel))
            {
                return Problem(FirmwareError.FirmwareInvalidChannel);
            }
            query = query.Where(v => v.Channel == firmwareChannel);
        }

        var total = await query.CountAsync(ct);

        var effectiveLimit = Math.Clamp(limit ?? DefaultLimit, 1, MaxLimit);
        var effectiveOffset = Math.Max(offset ?? 0, 0);

        var rows = await query
            .OrderByDescending(v => v.ReleaseDate)
            .Skip(effectiveOffset)
            .Take(effectiveLimit)
            .ToListAsync(ct);

        var summaries = rows
            .Select(v => new FirmwareVersionSummary
            {
                Version = v.Version,
                Channel = v.Channel.ToString().ToLowerInvariant(),
                ReleaseDate = v.ReleaseDate,
                Source = FirmwareSourceDto.From(v),
                ReleaseNotes = v.ReleaseNotes
                    .OrderBy(n => n.Index)
                    .Select(n => new FirmwareReleaseNoteDto
                    {
                        Type = n.SectionType.ToString().ToLowerInvariant(),
                        Title = n.Title,
                        Content = n.Content
                    })
                    .ToList()
            })
            .ToList();

        return Ok(new VersionListResponse { Versions = summaries, Total = total });
    }

    [HttpGet("{firmwareVersion}")]
    [CacheControl(86400, immutable: true)]
    public async Task<IActionResult> GetVersion([FromRoute] string firmwareVersion, CancellationToken ct)
    {
        var version = await _db.FirmwareVersions
            .Include(v => v.RepositoryNavigation)
            .Include(v => v.Artifacts)
            .Include(v => v.ReleaseNotes)
            .FirstOrDefaultAsync(v => v.Version == firmwareVersion, ct);

        if (version is null)
        {
            return Problem(FirmwareError.FirmwareVersionNotFound);
        }

        var boardIds = version.Artifacts.Select(a => a.BoardId).Distinct().ToList();
        var boards = await _db.FirmwareBoards
            .Include(b => b.ChipNavigation)
            .Where(b => boardIds.Contains(b.Id))
            .ToListAsync(ct);

        var cdnBase = _apiConfig.Firmware.CdnBaseUrl.TrimEnd('/');
        return Ok(FirmwareResponseMapper.ToReleaseDto(version, boards, cdnBase));
    }

    [HttpGet("{firmwareVersion}/{boardId}")]
    [CacheControl(86400, immutable: true)]
    public async Task<IActionResult> GetVersionForBoard(
        [FromRoute] string firmwareVersion,
        [FromRoute] string boardId,
        CancellationToken ct)
    {
        var exists = await _db.FirmwareVersions.AnyAsync(v => v.Version == firmwareVersion, ct);
        if (!exists)
        {
            return Problem(FirmwareError.FirmwareVersionNotFound);
        }

        var artifacts = await _db.FirmwareArtifacts
            .Where(a => a.Version == firmwareVersion && a.BoardId == boardId)
            .ToListAsync(ct);

        if (artifacts.Count == 0)
        {
            return Problem(FirmwareError.FirmwareBoardNotFound);
        }

        var cdnBase = _apiConfig.Firmware.CdnBaseUrl.TrimEnd('/');
        return Ok(new FirmwareBoardReleaseResponseDto
        {
            Version = firmwareVersion,
            BoardId = boardId,
            Artifacts = artifacts
                .Select(a => FirmwareResponseMapper.ToArtifactDto(a, firmwareVersion, cdnBase))
                .ToList()
        });
    }
}
