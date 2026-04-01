using System.Security.Cryptography;
using Asp.Versioning;
using FlexLabs.EntityFrameworkCore.Upsert;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OpenShock.RepositoryServer.Config;
using OpenShock.RepositoryServer.Enums;
using OpenShock.RepositoryServer.Models.Firmware;
using OpenShock.RepositoryServer.Problems;
using OpenShock.RepositoryServer.RepoServerDb;
using OpenShock.RepositoryServer.Services;
using OpenShock.RepositoryServer.Utils;
using Semver;

namespace OpenShock.RepositoryServer.Controllers.V2.Firmware;

[ApiVersion("2.0")]
[ApiController]
[Route("/v{version:apiVersion}/firmware/releases")]
[Authorize(AuthenticationSchemes = AuthSchemas.CiCdToken)]
public class ReleasesController : OpenShockControllerBase
{
    private readonly RepoServerContext _db;
    private readonly IStorageService _storage;
    private readonly ApiConfig _apiConfig;

    public ReleasesController(RepoServerContext db, IStorageService storage, ApiConfig apiConfig)
    {
        _db = db;
        _storage = storage;
        _apiConfig = apiConfig;
    }

    // ---- Init Release ----

    [HttpPost]
    public async Task<IActionResult> InitRelease([FromBody] InitReleaseRequest request, CancellationToken ct)
    {
        if (!SemVersion.TryParse(request.Version, SemVersionStyles.Strict, out _))
        {
            return Problem(FirmwareError.FirmwareInvalidSemver);
        }

        if (!Enum.TryParse<ReleaseChannel>(request.Channel, true, out var channel))
        {
            return Problem(FirmwareError.FirmwareInvalidChannel);
        }

        if (request.Boards.Count == 0)
        {
            return Problem(FirmwareError.FirmwareReleaseBoardsEmpty);
        }

        // Check no existing staging release for this version
        var existingStaging = await _db.FirmwareReleases
            .AnyAsync(r => r.Version == request.Version && r.Status == FirmwareReleaseStatus.Staging, ct);
        if (existingStaging)
        {
            return Problem(FirmwareError.FirmwareReleaseAlreadyStaging);
        }

        // Validate all declared boards exist
        var boardIds = request.Boards.ToHashSet();
        var existingBoardCount = await _db.FirmwareBoards.CountAsync(b => boardIds.Contains(b.Id), ct);
        if (existingBoardCount != boardIds.Count)
        {
            return Problem(FirmwareError.FirmwareBoardNotFound);
        }

        // Validate release note types
        foreach (var note in request.ReleaseNotes)
        {
            if (!Enum.TryParse<ReleaseNoteSectionType>(note.Type, true, out _))
            {
                return Problem(FirmwareError.FirmwareInvalidReleaseNoteType);
            }
        }

        var release = new FirmwareRelease
        {
            Id = Guid.NewGuid(),
            Version = request.Version,
            Channel = channel,
            CommitHash = request.CommitHash,
            ReleaseUrl = request.ReleaseUrl,
            ReleaseDate = request.ReleaseDate,
            Status = FirmwareReleaseStatus.Staging,
            DeclaredBoards = request.Boards.ToArray(),
            CreatedAt = DateTimeOffset.UtcNow,
        };

        _db.FirmwareReleases.Add(release);

        // Stage release notes
        for (var i = 0; i < request.ReleaseNotes.Count; i++)
        {
            var note = request.ReleaseNotes[i];
            _db.FirmwareStagedReleaseNotes.Add(new FirmwareStagedReleaseNote
            {
                ReleaseId = release.Id,
                Index = i,
                SectionType = Enum.Parse<ReleaseNoteSectionType>(note.Type, true),
                Title = note.Title,
                Content = note.Content,
            });
        }

        await _db.SaveChangesAsync(ct);

        return Created((string?)null, new InitReleaseResponse { Id = release.Id });
    }

    // ---- Upload Board Artifacts ----

    private static readonly Dictionary<string, FirmwareArtifactType> ArtifactFieldNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["app"] = FirmwareArtifactType.App,
        ["staticfs"] = FirmwareArtifactType.StaticFs,
        ["merged"] = FirmwareArtifactType.Merged,
        ["bootloader"] = FirmwareArtifactType.Bootloader,
        ["partitions"] = FirmwareArtifactType.Partitions,
    };

    [HttpPut("{releaseId:guid}/boards/{boardId}")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(64 * 1024 * 1024)]
    public async Task<IActionResult> UploadBoardArtifacts(
        [FromRoute] Guid releaseId,
        [FromRoute] string boardId,
        CancellationToken ct)
    {
        var release = await _db.FirmwareReleases.FirstOrDefaultAsync(r => r.Id == releaseId, ct);
        if (release == null)
        {
            return Problem(FirmwareError.FirmwareReleaseNotFound);
        }

        if (release.Status != FirmwareReleaseStatus.Staging)
        {
            return Problem(FirmwareError.FirmwareReleaseNotStaging);
        }

        if (!release.DeclaredBoards.Contains(boardId))
        {
            return Problem(FirmwareError.FirmwareBoardNotDeclared);
        }

        var board = await _db.FirmwareBoards.FirstOrDefaultAsync(b => b.Id == boardId, ct);
        if (board == null)
        {
            return Problem(FirmwareError.FirmwareBoardNotFound);
        }

        var files = Request.Form.Files;
        if (files.Count == 0)
        {
            return BadRequest(new { error = "No files uploaded. Expected file fields: app, staticfs, merged, bootloader, partitions" });
        }

        // Validate all required artifact types are present
        if (board.RequiredArtifactTypes.Length > 0)
        {
            var uploadedTypes = files
                .Select(f => f.Name.ToLowerInvariant())
                .Where(ArtifactFieldNames.ContainsKey)
                .Select(n => ArtifactFieldNames[n])
                .ToHashSet();
            var missingTypes = board.RequiredArtifactTypes
                .Where(r => !uploadedTypes.Contains(r))
                .Select(r => r.ToString().ToLowerInvariant())
                .ToList();
            if (missingTypes.Count > 0)
            {
                return Problem(FirmwareError.FirmwareMissingRequiredArtifacts(boardId, missingTypes));
            }
        }

        // Remove any previously staged artifacts for this board in this release
        await _db.FirmwareStagedArtifacts
            .Where(a => a.ReleaseId == releaseId && a.BoardId == boardId)
            .ExecuteDeleteAsync(ct);

        var uploadedArtifacts = new List<FirmwareArtifactDto>();
        var cdnBase = _apiConfig.Firmware.CdnBaseUrl.TrimEnd('/');

        foreach (var file in files)
        {
            var fieldName = file.Name.ToLowerInvariant();
            if (!ArtifactFieldNames.TryGetValue(fieldName, out var artifactType))
            {
                return Problem(FirmwareError.FirmwareInvalidArtifactType);
            }

            using var memoryStream = new MemoryStream();
            await file.CopyToAsync(memoryStream, ct);
            var fileBytes = memoryStream.ToArray();

            var hashBytes = SHA256.HashData(fileBytes);

            // Upload to CDN at final path
            var cdnFileName = FirmwareArtifactFileNames.GetFileName(artifactType);
            var cdnPath = $"{release.Version}/{boardId}/{cdnFileName}";

            using var uploadStream = new MemoryStream(fileBytes);
            await _storage.UploadFileAsync(cdnPath, uploadStream, ct);

            // Stage artifact record
            _db.FirmwareStagedArtifacts.Add(new FirmwareStagedArtifact
            {
                ReleaseId = releaseId,
                BoardId = boardId,
                ArtifactType = artifactType,
                HashSha256 = hashBytes,
                FileSize = fileBytes.Length,
            });

            uploadedArtifacts.Add(new FirmwareArtifactDto
            {
                Type = artifactType.ToString().ToLowerInvariant(),
                Url = $"{cdnBase}/{release.Version}/{boardId}/{cdnFileName}",
                Sha256Hash = Convert.ToHexString(hashBytes),
                FileSize = fileBytes.Length,
            });
        }

        await _db.SaveChangesAsync(ct);

        return Ok(uploadedArtifacts);
    }

    // ---- Publish Release ----

    [HttpPost("{releaseId:guid}/publish")]
    public async Task<IActionResult> PublishRelease([FromRoute] Guid releaseId, CancellationToken ct)
    {
        var release = await _db.FirmwareReleases
            .Include(r => r.StagedArtifacts)
            .Include(r => r.StagedReleaseNotes.OrderBy(n => n.Index))
            .FirstOrDefaultAsync(r => r.Id == releaseId, ct);

        if (release == null)
        {
            return Problem(FirmwareError.FirmwareReleaseNotFound);
        }

        if (release.Status != FirmwareReleaseStatus.Staging)
        {
            return Problem(FirmwareError.FirmwareReleaseNotStaging);
        }

        // Check all declared boards have staged artifacts
        var uploadedBoardIds = release.StagedArtifacts.Select(a => a.BoardId).Distinct().ToHashSet();
        var missingBoards = release.DeclaredBoards.Where(b => !uploadedBoardIds.Contains(b)).ToList();
        if (missingBoards.Count > 0)
        {
            return Problem(FirmwareError.FirmwareReleaseIncomplete(missingBoards));
        }

        // Validate required artifact types per board
        var boards = await _db.FirmwareBoards
            .Where(b => release.DeclaredBoards.Contains(b.Id))
            .ToDictionaryAsync(b => b.Id, ct);

        foreach (var boardId in release.DeclaredBoards)
        {
            if (!boards.TryGetValue(boardId, out var board) || board.RequiredArtifactTypes.Length == 0)
                continue;

            var stagedTypes = release.StagedArtifacts
                .Where(a => a.BoardId == boardId)
                .Select(a => a.ArtifactType)
                .ToHashSet();
            var missingTypes = board.RequiredArtifactTypes
                .Where(r => !stagedTypes.Contains(r))
                .Select(r => r.ToString().ToLowerInvariant())
                .ToList();
            if (missingTypes.Count > 0)
            {
                return Problem(FirmwareError.FirmwareMissingRequiredArtifacts(boardId, missingTypes));
            }
        }

        await using var transaction = await _db.Database.BeginTransactionAsync(ct);

        // Upsert firmware version
        var versionEntity = new FirmwareVersion
        {
            Version = release.Version,
            Channel = release.Channel,
            ReleaseDate = release.ReleaseDate,
            CommitHash = release.CommitHash,
            ReleaseUrl = release.ReleaseUrl,
        };
        await _db.FirmwareVersions.Upsert(versionEntity).On(v => v.Version).RunAsync(ct);

        // Replace artifacts
        await _db.FirmwareArtifacts
            .Where(a => a.Version == release.Version)
            .ExecuteDeleteAsync(ct);

        foreach (var staged in release.StagedArtifacts)
        {
            _db.FirmwareArtifacts.Add(new FirmwareArtifact
            {
                Version = release.Version,
                BoardId = staged.BoardId,
                ArtifactType = staged.ArtifactType,
                HashSha256 = staged.HashSha256,
                FileSize = staged.FileSize,
            });
        }

        // Replace release notes
        await _db.FirmwareReleaseNotes
            .Where(n => n.Version == release.Version)
            .ExecuteDeleteAsync(ct);

        foreach (var staged in release.StagedReleaseNotes)
        {
            _db.FirmwareReleaseNotes.Add(new FirmwareReleaseNote
            {
                Version = release.Version,
                Index = staged.Index,
                SectionType = staged.SectionType,
                Title = staged.Title,
                Content = staged.Content,
            });
        }

        release.Status = FirmwareReleaseStatus.Published;

        await _db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        return CreatedAtAction(
            nameof(VersionsController.GetVersion),
            "Versions",
            new { firmwareVersion = release.Version },
            null);
    }

    // ---- Abort Release ----

    [HttpDelete("{releaseId:guid}")]
    public async Task<IActionResult> AbortRelease([FromRoute] Guid releaseId, CancellationToken ct)
    {
        var release = await _db.FirmwareReleases
            .Include(r => r.StagedArtifacts)
            .FirstOrDefaultAsync(r => r.Id == releaseId, ct);

        if (release == null)
        {
            return Problem(FirmwareError.FirmwareReleaseNotFound);
        }

        if (release.Status != FirmwareReleaseStatus.Staging)
        {
            return Problem(FirmwareError.FirmwareReleaseNotStaging);
        }

        // Delete uploaded CDN files for each staged artifact
        foreach (var artifact in release.StagedArtifacts)
        {
            var cdnFileName = FirmwareArtifactFileNames.GetFileName(artifact.ArtifactType);
            var cdnPath = $"{release.Version}/{artifact.BoardId}/{cdnFileName}";
            await _storage.DeleteFileAsync(cdnPath, ct);
        }

        release.Status = FirmwareReleaseStatus.Aborted;

        await _db.SaveChangesAsync(ct);

        return Ok();
    }
}
