using System.Security.Cryptography;
using System.Security.Claims;
using System.Text.Json;
using Asp.Versioning;
using FlexLabs.EntityFrameworkCore.Upsert;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OneOf;
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
    private static readonly Dictionary<string, FirmwareArtifactType> ArtifactFieldNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["app"] = FirmwareArtifactType.App,
        ["staticfs"] = FirmwareArtifactType.StaticFs,
        ["merged"] = FirmwareArtifactType.Merged,
        ["bootloader"] = FirmwareArtifactType.Bootloader,
        ["partitions"] = FirmwareArtifactType.Partitions,
    };

    private readonly RepoServerContext _db;
    private readonly IStorageService _storage;
    private readonly ApiConfig _apiConfig;
    private readonly IDiscordNotificationService _discord;
    private readonly TimeProvider _timeProvider;

    public ReleasesController(
        RepoServerContext db,
        IStorageService storage,
        ApiConfig apiConfig,
        IDiscordNotificationService discord,
        TimeProvider timeProvider)
    {
        _db = db;
        _storage = storage;
        _apiConfig = apiConfig;
        _discord = discord;
        _timeProvider = timeProvider;
    }

    // ---- Init Release ----

    [HttpPost]
    public async Task<IActionResult> InitRelease(
        [FromBody] InitReleaseRequest request,
        [FromQuery(Name = "nofail")] bool? nofailQuery,
        CancellationToken ct)
    {
        var nofail = nofailQuery == true || Request.Query.ContainsKey("nofail");

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

        var existingStaging = await _db.FirmwareReleases
            .AnyAsync(r => r.Version == request.Version &&
                           (r.Status == ReleaseStatus.Staging || r.Status == ReleaseStatus.Editing),
                ct);
        if (existingStaging)
        {
            return Problem(FirmwareError.FirmwareReleaseAlreadyStaging);
        }

        var boardIds = request.Boards.ToHashSet();
        var existingBoardCount = await _db.FirmwareBoards.CountAsync(b => boardIds.Contains(b.Id), ct);
        if (existingBoardCount != boardIds.Count)
        {
            return Problem(FirmwareError.FirmwareBoardNotFound);
        }

        // Source traceability — from OIDC claims (attached by GitHubOidcAuthentication).
        if (!TryReadSourceClaims(out var sourceClaims, out var missing))
        {
            return Problem(FirmwareError.FirmwareInvalidChangelog($"Missing OIDC source claims: {missing}"));
        }

        // Parse changelog.
        var parseResult = ChangelogParser.Parse(request.Changelog);
        ReleaseStatus status;
        IReadOnlyList<FirmwareReleaseNoteDto> notes;

        if (parseResult.TryPickT0(out var parsedNotes, out var error))
        {
            status = ReleaseStatus.Staging;
            notes = parsedNotes;
        }
        else if (nofail)
        {
            status = ReleaseStatus.Editing;
            notes = Array.Empty<FirmwareReleaseNoteDto>();
        }
        else
        {
            return Problem(FirmwareError.FirmwareInvalidChangelog(DescribeParseError(error)));
        }

        var release = new FirmwareRelease
        {
            Id = Guid.NewGuid(),
            Version = request.Version,
            Channel = channel,
            RepositoryId = sourceClaims.RepositoryId,
            CommitHash = sourceClaims.CommitHash,
            Ref = sourceClaims.Ref,
            RunId = sourceClaims.RunId,
            ReleaseDate = request.ReleaseDate,
            Status = status,
            DeclaredBoards = request.Boards.ToArray(),
            CreatedAt = _timeProvider.GetUtcNow(),
        };

        _db.FirmwareReleases.Add(release);

        for (var i = 0; i < notes.Count; i++)
        {
            var n = notes[i];
            _db.FirmwareStagedReleaseNotes.Add(new FirmwareStagedReleaseNote
            {
                ReleaseId = release.Id,
                Index = i,
                SectionType = Enum.Parse<ReleaseNoteSectionType>(n.Type, true),
                Title = n.Title,
                Content = n.Content,
            });
        }

        await _db.SaveChangesAsync(ct);

        if (status == ReleaseStatus.Editing)
        {
            await _discord.NotifyReleaseNotesNeedEditingAsync(release.Id, release.Version, channel.ToString().ToLowerInvariant(), ct);
        }

        return Created((string?)null, new InitReleaseResponse
        {
            Id = release.Id,
            Status = status.ToString().ToLowerInvariant()
        });
    }

    // ---- Upload Board Artifacts ----

    [HttpPut("{releaseId:guid}/boards/{boardId}")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(64 * 1024 * 1024)]
    public async Task<IActionResult> UploadBoardArtifacts(
        [FromRoute] Guid releaseId,
        [FromRoute] string boardId,
        CancellationToken ct)
    {
        var release = await _db.FirmwareReleases.FirstOrDefaultAsync(r => r.Id == releaseId, ct);
        if (release is null)
        {
            return Problem(FirmwareError.FirmwareReleaseNotFound);
        }

        if (release.Status != ReleaseStatus.Staging && release.Status != ReleaseStatus.Editing)
        {
            return Problem(FirmwareError.FirmwareReleaseNotEditable);
        }

        if (!release.DeclaredBoards.Contains(boardId))
        {
            return Problem(FirmwareError.FirmwareBoardNotDeclared);
        }

        var board = await _db.FirmwareBoards.FirstOrDefaultAsync(b => b.Id == boardId, ct);
        if (board is null)
        {
            return Problem(FirmwareError.FirmwareBoardNotFound);
        }

        var files = Request.Form.Files;
        if (files.Count == 0)
        {
            return Problem(FirmwareError.FirmwareManifestKeysMismatch("No artifact files uploaded"));
        }

        var sha256FormValue = Request.Form["sha256"].ToString();
        if (string.IsNullOrWhiteSpace(sha256FormValue))
        {
            return Problem(FirmwareError.FirmwareManifestKeysMismatch("Missing required 'sha256' form field"));
        }

        Dictionary<string, string>? expectedHashes;
        try
        {
            expectedHashes = JsonSerializer.Deserialize<Dictionary<string, string>>(
                sha256FormValue,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException)
        {
            return Problem(FirmwareError.FirmwareManifestKeysMismatch("'sha256' form field is not valid JSON"));
        }

        if (expectedHashes is null)
        {
            return Problem(FirmwareError.FirmwareManifestKeysMismatch("'sha256' form field is null"));
        }

        // Validate file field names and build an uploaded-type map.
        var uploadedByType = new Dictionary<FirmwareArtifactType, IFormFile>();
        foreach (var file in files)
        {
            if (!ArtifactFieldNames.TryGetValue(file.Name, out var artifactType))
            {
                return Problem(FirmwareError.FirmwareInvalidArtifactType);
            }
            uploadedByType[artifactType] = file;
        }

        // Normalize expected manifest keys to enum values.
        var normalizedExpected = new Dictionary<FirmwareArtifactType, string>();
        foreach (var kv in expectedHashes)
        {
            if (!ArtifactFieldNames.TryGetValue(kv.Key, out var artifactType))
            {
                return Problem(FirmwareError.FirmwareInvalidArtifactType);
            }
            normalizedExpected[artifactType] = kv.Value;
        }

        // Keys must match exactly (spec §5.2).
        var onlyInFiles = uploadedByType.Keys.Except(normalizedExpected.Keys).ToList();
        var onlyInManifest = normalizedExpected.Keys.Except(uploadedByType.Keys).ToList();
        if (onlyInFiles.Count > 0 || onlyInManifest.Count > 0)
        {
            var detail = $"Uploaded-only: [{string.Join(", ", onlyInFiles)}]; manifest-only: [{string.Join(", ", onlyInManifest)}]";
            return Problem(FirmwareError.FirmwareManifestKeysMismatch(detail));
        }

        // Validate required artifact types per board config.
        if (board.RequiredArtifactTypes.Length > 0)
        {
            var missingRequired = board.RequiredArtifactTypes
                .Where(r => !uploadedByType.ContainsKey(r))
                .Select(r => r.ToString().ToLowerInvariant())
                .ToList();
            if (missingRequired.Count > 0)
            {
                return Problem(FirmwareError.FirmwareMissingRequiredArtifacts(boardId, missingRequired));
            }
        }

        // Remove any previously staged artifacts for this board in this release.
        await _db.FirmwareStagedArtifacts
            .Where(a => a.ReleaseId == releaseId && a.BoardId == boardId)
            .ExecuteDeleteAsync(ct);

        var cdnBase = _apiConfig.Firmware.CdnBaseUrl.TrimEnd('/');
        var uploadedArtifacts = new List<FirmwareArtifactDto>();
        var mismatches = new List<string>();

        foreach (var (artifactType, file) in uploadedByType)
        {
            await using var memory = new MemoryStream();
            await file.CopyToAsync(memory, ct);
            var bytes = memory.ToArray();
            var actual = Convert.ToHexString(SHA256.HashData(bytes));

            var expected = normalizedExpected[artifactType];
            if (!string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
            {
                mismatches.Add($"{artifactType.ToString().ToLowerInvariant()} (expected={expected}, actual={actual})");
                continue;
            }

            var cdnFileName = FirmwareArtifactFileNames.GetFileName(artifactType);
            var cdnPath = $"{release.Version}/{boardId}/{cdnFileName}";

            await using var uploadStream = new MemoryStream(bytes);
            await _storage.UploadFileAsync(cdnPath, uploadStream, ct);

            _db.FirmwareStagedArtifacts.Add(new FirmwareStagedArtifact
            {
                ReleaseId = releaseId,
                BoardId = boardId,
                ArtifactType = artifactType,
                HashSha256 = Convert.FromHexString(actual),
                FileSize = bytes.Length,
            });

            uploadedArtifacts.Add(new FirmwareArtifactDto
            {
                Type = artifactType.ToString().ToLowerInvariant(),
                Url = $"{cdnBase}/{release.Version}/{boardId}/{cdnFileName}",
                Sha256Hash = actual,
                FileSize = bytes.Length,
            });
        }

        if (mismatches.Count > 0)
        {
            return Problem(FirmwareError.FirmwareSha256Mismatch(string.Join("; ", mismatches)));
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

        if (release is null)
        {
            return Problem(FirmwareError.FirmwareReleaseNotFound);
        }

        if (release.Status == ReleaseStatus.Editing)
        {
            return Problem(FirmwareError.FirmwareReleaseNotesNotFinalized);
        }

        if (release.Status != ReleaseStatus.Staging)
        {
            return Problem(FirmwareError.FirmwareReleaseNotStaging);
        }

        var uploadedBoardIds = release.StagedArtifacts.Select(a => a.BoardId).Distinct().ToHashSet();
        var missingBoards = release.DeclaredBoards.Where(b => !uploadedBoardIds.Contains(b)).ToList();
        if (missingBoards.Count > 0)
        {
            return Problem(FirmwareError.FirmwareReleaseIncomplete(missingBoards));
        }

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

        var versionEntity = new FirmwareVersion
        {
            Version = release.Version,
            Channel = release.Channel,
            ReleaseDate = release.ReleaseDate,
            RepositoryId = release.RepositoryId,
            CommitHash = release.CommitHash,
            Ref = release.Ref,
            RunId = release.RunId,
        };
        await _db.FirmwareVersions.Upsert(versionEntity).On(v => v.Version).RunAsync(ct);

        await _db.FirmwareArtifacts.Where(a => a.Version == release.Version).ExecuteDeleteAsync(ct);
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

        await _db.FirmwareReleaseNotes.Where(n => n.Version == release.Version).ExecuteDeleteAsync(ct);
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

        release.Status = ReleaseStatus.Published;

        await _db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        await _discord.NotifyFirmwareReleasePublishedAsync(
            release.Version,
            release.Channel.ToString().ToLowerInvariant(),
            release.CommitHash,
            ct);

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

        if (release is null)
        {
            return Problem(FirmwareError.FirmwareReleaseNotFound);
        }

        if (release.Status != ReleaseStatus.Staging && release.Status != ReleaseStatus.Editing)
        {
            return Problem(FirmwareError.FirmwareReleaseNotEditable);
        }

        foreach (var artifact in release.StagedArtifacts)
        {
            var cdnFileName = FirmwareArtifactFileNames.GetFileName(artifact.ArtifactType);
            var cdnPath = $"{release.Version}/{artifact.BoardId}/{cdnFileName}";
            await _storage.DeleteFileAsync(cdnPath, ct);
        }

        release.Status = ReleaseStatus.Aborted;
        await _db.SaveChangesAsync(ct);

        return NoContent();
    }

    // ---- Helpers ----

    private readonly record struct SourceClaims(Guid RepositoryId, string CommitHash, string? Ref, string? RunId);

    private bool TryReadSourceClaims(out SourceClaims claims, out string missing)
    {
        var rawRepoId = User.FindFirstValue(AuthSchemas.CiCdClaims.RepositoryId);
        var commitHash = User.FindFirstValue(AuthSchemas.CiCdClaims.CommitHash);
        var refValue = User.FindFirstValue(AuthSchemas.CiCdClaims.Ref);
        var runId = User.FindFirstValue(AuthSchemas.CiCdClaims.RunId);

        if (string.IsNullOrWhiteSpace(rawRepoId) || !Guid.TryParse(rawRepoId, out var repoId))
        {
            claims = default;
            missing = AuthSchemas.CiCdClaims.RepositoryId;
            return false;
        }
        if (string.IsNullOrWhiteSpace(commitHash))
        {
            claims = default;
            missing = AuthSchemas.CiCdClaims.CommitHash;
            return false;
        }

        claims = new SourceClaims(repoId, commitHash, refValue, runId);
        missing = string.Empty;
        return true;
    }

    private static string DescribeParseError(ChangelogParseError error) => error switch
    {
        ChangelogParseError.Empty => "Changelog is empty or whitespace-only",
        ChangelogParseError.NoHeadings => "Changelog contains no '### Heading' sections",
        ChangelogParseError.AllSectionsEmpty => "All changelog sections are empty",
        _ => error.ToString()
    };
}
