using System.Security.Cryptography;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OpenShock.RepositoryServer.Config;
using OpenShock.RepositoryServer.Models.Firmware;
using OpenShock.RepositoryServer.Problems;
using OpenShock.RepositoryServer.RepoServerDb;
using OpenShock.RepositoryServer.Services;
using OpenShock.RepositoryServer.Utils;
using Semver;

namespace OpenShock.RepositoryServer.Controllers.V2.Firmware;

[ApiVersion("2.0")]
[ApiController]
[Route("/v{version:apiVersion}/firmware/admin")]
[Authorize(AuthenticationSchemes = AuthSchemas.AdminToken)]
public class AdminController : OpenShockControllerBase
{
    private readonly RepoServerContext _db;
    private readonly CdnStorageService _cdn;
    private readonly ApiConfig _apiConfig;

    public AdminController(RepoServerContext db, CdnStorageService cdn, ApiConfig apiConfig)
    {
        _db = db;
        _cdn = cdn;
        _apiConfig = apiConfig;
    }

    // ---- Version Management ----

    [HttpPut("versions/{firmwareVersion}")]
    public async Task<IActionResult> UpsertVersion([FromRoute] string firmwareVersion, [FromBody] CreateFirmwareVersionRequest request)
    {
        if (!SemVersion.TryParse(firmwareVersion, SemVersionStyles.Strict, out _))
        {
            return Problem(FirmwareError.FirmwareInvalidSemver);
        }

        if (!Enum.TryParse<FirmwareChannel>(request.Channel, true, out var channel))
        {
            return Problem(FirmwareError.FirmwareInvalidChannel);
        }

        // Validate artifacts if provided
        if (request.Artifacts is { Count: > 0 })
        {
            var boardIds = request.Artifacts.Keys.ToHashSet();
            var existingBoards = await _db.FirmwareBoards
                .Where(b => boardIds.Contains(b.Id))
                .Select(b => b.Id)
                .ToHashSetAsync();

            var missingBoards = boardIds.Except(existingBoards).ToList();
            if (missingBoards.Count > 0)
            {
                return Problem(FirmwareError.FirmwareBoardNotFound);
            }

            foreach (var (boardId, artifactTypes) in request.Artifacts)
            {
                foreach (var artifactType in artifactTypes.Keys)
                {
                    if (!Enum.TryParse<FirmwareArtifactType>(artifactType, true, out _))
                    {
                        return Problem(FirmwareError.FirmwareInvalidArtifactType);
                    }
                }
            }
        }

        // Validate release note types
        foreach (var note in request.ReleaseNotes)
        {
            if (!Enum.TryParse<FirmwareReleaseNoteType>(note.Type, true, out _))
            {
                return Problem(FirmwareError.FirmwareInvalidReleaseNoteType);
            }
        }

        await using var transaction = await _db.Database.BeginTransactionAsync();

        // Upsert the firmware version
        var versionEntity = new FirmwareVersion
        {
            Version = firmwareVersion,
            Channel = channel,
            ReleaseDate = request.ReleaseDate,
            CommitHash = request.CommitHash,
            ReleaseUrl = request.ReleaseUrl
        };
        var executed = await _db.FirmwareVersions.Upsert(versionEntity).On(v => v.Version).RunAsync();
        if (executed <= 0) throw new Exception("Failed to upsert firmware version");

        // Replace artifacts only if provided in the request
        if (request.Artifacts is { Count: > 0 })
        {
            await _db.FirmwareArtifacts.Where(a => a.Version == firmwareVersion).ExecuteDeleteAsync();
            foreach (var (boardId, artifactTypes) in request.Artifacts)
            {
                foreach (var (artifactTypeStr, upload) in artifactTypes)
                {
                    var artifactType = Enum.Parse<FirmwareArtifactType>(artifactTypeStr, true);
                    _db.FirmwareArtifacts.Add(new FirmwareArtifact
                    {
                        Version = firmwareVersion,
                        BoardId = boardId,
                        ArtifactType = artifactType,
                        HashSha256 = Convert.FromHexString(upload.Sha256Hash),
                        FileSize = upload.FileSize
                    });
                }
            }
        }

        // Replace all release notes for this version
        await _db.FirmwareReleaseNotes.Where(n => n.Version == firmwareVersion).ExecuteDeleteAsync();
        for (var i = 0; i < request.ReleaseNotes.Count; i++)
        {
            var note = request.ReleaseNotes[i];
            _db.FirmwareReleaseNotes.Add(new FirmwareReleaseNote
            {
                Version = firmwareVersion,
                Index = i,
                Type = Enum.Parse<FirmwareReleaseNoteType>(note.Type, true),
                Title = note.Title,
                Content = note.Content
            });
        }

        await _db.SaveChangesAsync();
        await transaction.CommitAsync();

        return Created();
    }

    [HttpDelete("versions/{firmwareVersion}")]
    public async Task<IActionResult> DeleteVersion([FromRoute] string firmwareVersion)
    {
        var deleted = await _db.FirmwareVersions.Where(v => v.Version == firmwareVersion).ExecuteDeleteAsync();
        if (deleted <= 0) return Problem(FirmwareError.FirmwareVersionNotFound);
        return Ok();
    }

    // ---- Board Artifact Upload ----

    private static readonly Dictionary<string, FirmwareArtifactType> ArtifactFieldNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["app"] = FirmwareArtifactType.App,
        ["staticfs"] = FirmwareArtifactType.StaticFs,
        ["merged"] = FirmwareArtifactType.Merged,
        ["bootloader"] = FirmwareArtifactType.Bootloader,
        ["partitions"] = FirmwareArtifactType.Partitions,
    };

    /// <summary>
    /// Uploads firmware binary artifacts for a specific board+version.
    /// Accepts multipart/form-data with file fields named: app, staticfs, merged, bootloader, partitions.
    /// Each file is hashed, uploaded to CDN, and recorded in the database.
    /// </summary>
    [HttpPut("versions/{firmwareVersion}/boards/{boardId}/upload")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(64 * 1024 * 1024)] // 64 MB
    public async Task<IActionResult> UploadBoardArtifacts(
        [FromRoute] string firmwareVersion,
        [FromRoute] string boardId)
    {
        if (!SemVersion.TryParse(firmwareVersion, SemVersionStyles.Strict, out _))
        {
            return Problem(FirmwareError.FirmwareInvalidSemver);
        }

        if (!await _db.FirmwareVersions.AnyAsync(v => v.Version == firmwareVersion))
        {
            return Problem(FirmwareError.FirmwareVersionNotFound);
        }

        if (!await _db.FirmwareBoards.AnyAsync(b => b.Id == boardId))
        {
            return Problem(FirmwareError.FirmwareBoardNotFound);
        }

        var files = Request.Form.Files;
        if (files.Count == 0)
        {
            return BadRequest(new { error = "No files uploaded. Expected file fields: app, staticfs, merged, bootloader, partitions" });
        }

        var uploadedArtifacts = new List<FirmwareArtifactDto>();

        foreach (var file in files)
        {
            var fieldName = file.Name.ToLowerInvariant();
            if (!ArtifactFieldNames.TryGetValue(fieldName, out var artifactType))
            {
                return Problem(FirmwareError.FirmwareInvalidArtifactType);
            }

            // Read file into memory for hashing + CDN upload
            using var memoryStream = new MemoryStream();
            await file.CopyToAsync(memoryStream);
            var fileBytes = memoryStream.ToArray();
            var fileSize = fileBytes.Length;

            // Compute SHA256 hash
            var hashBytes = SHA256.HashData(fileBytes);

            // Upload to CDN
            var cdnFileName = FirmwareArtifactFileNames.GetFileName(artifactType);
            var cdnPath = $"{firmwareVersion}/{boardId}/{cdnFileName}";

            using var uploadStream = new MemoryStream(fileBytes);
            await _cdn.UploadFileAsync(cdnPath, uploadStream);

            // Upsert artifact record in DB
            var existing = await _db.FirmwareArtifacts.FirstOrDefaultAsync(a =>
                a.Version == firmwareVersion && a.BoardId == boardId && a.ArtifactType == artifactType);

            if (existing != null)
            {
                existing.HashSha256 = hashBytes;
                existing.FileSize = fileSize;
            }
            else
            {
                _db.FirmwareArtifacts.Add(new FirmwareArtifact
                {
                    Version = firmwareVersion,
                    BoardId = boardId,
                    ArtifactType = artifactType,
                    HashSha256 = hashBytes,
                    FileSize = fileSize,
                });
            }

            var cdnBase = _apiConfig.Firmware.CdnBaseUrl.TrimEnd('/');
            uploadedArtifacts.Add(new FirmwareArtifactDto
            {
                Type = artifactType.ToString().ToLowerInvariant(),
                Url = $"{cdnBase}/{firmwareVersion}/{boardId}/{cdnFileName}",
                Sha256Hash = Convert.ToHexString(hashBytes),
                FileSize = fileSize,
            });
        }

        await _db.SaveChangesAsync();

        return Ok(uploadedArtifacts);
    }

    // ---- Board Management ----

    [HttpPut("boards/{boardId}")]
    public async Task<IActionResult> UpsertBoard([FromRoute] string boardId, [FromBody] CreateFirmwareBoardRequest request)
    {
        if (!await _db.FirmwareChips.AnyAsync(c => c.Id == request.ChipId))
        {
            return Problem(FirmwareError.FirmwareChipNotFound);
        }

        var board = new FirmwareBoard
        {
            Id = boardId,
            Name = request.Name,
            ChipId = request.ChipId,
            Discontinued = false
        };
        var executed = await _db.FirmwareBoards.Upsert(board).On(b => b.Id)
            .WhenMatched(b => new FirmwareBoard
            {
                Name = board.Name,
                ChipId = board.ChipId
            })
            .RunAsync();
        if (executed <= 0) throw new Exception("Failed to upsert firmware board");

        return Created();
    }

    [HttpPatch("boards/{boardId}/discontinue")]
    public async Task<IActionResult> DiscontinueBoard([FromRoute] string boardId)
    {
        var board = await _db.FirmwareBoards.FirstOrDefaultAsync(b => b.Id == boardId);
        if (board == null) return Problem(FirmwareError.FirmwareBoardNotFound);

        board.Discontinued = true;
        await _db.SaveChangesAsync();
        return Ok();
    }

    [HttpDelete("boards/{boardId}")]
    public async Task<IActionResult> DeleteBoard([FromRoute] string boardId)
    {
        if (await _db.FirmwareArtifacts.AnyAsync(a => a.BoardId == boardId))
        {
            return Problem(FirmwareError.FirmwareBoardInUse);
        }

        var deleted = await _db.FirmwareBoards.Where(b => b.Id == boardId).ExecuteDeleteAsync();
        if (deleted <= 0) return Problem(FirmwareError.FirmwareBoardNotFound);
        return Ok();
    }

    // ---- Chip Management ----

    [HttpPut("chips/{chipId}")]
    public async Task<IActionResult> UpsertChip([FromRoute] string chipId, [FromBody] CreateFirmwareChipRequest request)
    {
        FirmwareChipArchitecture? architecture = null;
        if (request.Architecture != null)
        {
            if (!Enum.TryParse<FirmwareChipArchitecture>(request.Architecture, true, out var parsed))
            {
                return Problem(FirmwareError.FirmwareInvalidArchitecture);
            }
            architecture = parsed;
        }

        var chip = new FirmwareChip
        {
            Id = chipId,
            Name = request.Name,
            Architecture = architecture
        };
        var executed = await _db.FirmwareChips.Upsert(chip).On(c => c.Id).RunAsync();
        if (executed <= 0) throw new Exception("Failed to upsert firmware chip");

        return Created();
    }

    [HttpDelete("chips/{chipId}")]
    public async Task<IActionResult> DeleteChip([FromRoute] string chipId)
    {
        if (await _db.FirmwareBoards.AnyAsync(b => b.ChipId == chipId))
        {
            return Problem(FirmwareError.FirmwareChipInUse);
        }

        var deleted = await _db.FirmwareChips.Where(c => c.Id == chipId).ExecuteDeleteAsync();
        if (deleted <= 0) return Problem(FirmwareError.FirmwareChipNotFound);
        return Ok();
    }
}
