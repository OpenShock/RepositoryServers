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
[Route("/v{version:apiVersion}/firmware/cicd")]
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

    // ---- Version Publishing ----

    [HttpPut("versions/{firmwareVersion}")]
    public async Task<IActionResult> PublishVersion([FromRoute] string firmwareVersion, [FromBody] CreateFirmwareVersionRequest request)
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
            var boards = await _db.FirmwareBoards
                .Where(b => boardIds.Contains(b.Id))
                .ToDictionaryAsync(b => b.Id);

            var missingBoardsCount = boardIds.Except(boards.Keys).Count();
            if (missingBoardsCount > 0)
            {
                return Problem(FirmwareError.FirmwareBoardNotFound);
            }

            foreach (var (boardId, artifactTypes) in request.Artifacts)
            {
                if (artifactTypes.Keys.Any(artifactType => !Enum.TryParse<FirmwareArtifactType>(artifactType, true, out _)))
                {
                    return Problem(FirmwareError.FirmwareInvalidArtifactType);
                }

                // Validate all required artifact types are present
                var board = boards[boardId];
                if (board.RequiredArtifactTypes.Length > 0)
                {
                    var providedTypes = artifactTypes.Keys
                        .Select(k => Enum.Parse<FirmwareArtifactType>(k, true))
                        .ToHashSet();
                    var missingTypes = board.RequiredArtifactTypes
                        .Where(r => !providedTypes.Contains(r))
                        .Select(r => r.ToString().ToLowerInvariant())
                        .ToList();
                    if (missingTypes.Count > 0)
                    {
                        return Problem(FirmwareError.FirmwareMissingRequiredArtifacts(boardId, missingTypes));
                    }
                }
            }
        }

        // Validate release note types
        if (request.ReleaseNotes.Any(note => !Enum.TryParse<FirmwareReleaseNoteType>(note.Type, true, out _)))
        {
            return Problem(FirmwareError.FirmwareInvalidReleaseNoteType);
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

        var board = await _db.FirmwareBoards.FirstOrDefaultAsync(b => b.Id == boardId);
        if (board == null)
        {
            return Problem(FirmwareError.FirmwareBoardNotFound);
        }

        var files = Request.Form.Files;
        if (files.Count == 0)
        {
            return BadRequest(new { error = "No files uploaded. Expected file fields: app, staticfs, merged, bootloader, partitions" });
        }

        // Validate all required artifact types are present in the upload
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

            // Upload to storage
            var cdnFileName = FirmwareArtifactFileNames.GetFileName(artifactType);
            var cdnPath = $"{firmwareVersion}/{boardId}/{cdnFileName}";

            using var uploadStream = new MemoryStream(fileBytes);
            await _storage.UploadFileAsync(cdnPath, uploadStream);

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
}
