using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OpenShock.RepositoryServer.Models.Firmware;
using OpenShock.RepositoryServer.Problems;
using OpenShock.RepositoryServer.RepoServerDb;
using Semver;

namespace OpenShock.RepositoryServer.Controllers.V2.Firmware;

[ApiVersion("2.0")]
[ApiController]
[Route("/v{version:apiVersion}/firmware/admin")]
[Authorize(AuthenticationSchemes = AuthSchemas.AdminToken)]
public class AdminController : OpenShockControllerBase
{
    private readonly RepoServerContext _db;

    public AdminController(RepoServerContext db)
    {
        _db = db;
    }

    // ---- Version Management ----

    [HttpPut("versions/{firmwareVersion}")]
    public async Task<IActionResult> UpsertVersion([FromRoute] string firmwareVersion, [FromBody] CreateFirmwareVersionRequest request)
    {
        if (!SemVersion.TryParse(firmwareVersion, SemVersionStyles.Any, out _))
        {
            return Problem(FirmwareError.FirmwareInvalidSemver);
        }

        if (!Enum.TryParse<FirmwareChannel>(request.Channel, true, out var channel))
        {
            return Problem(FirmwareError.FirmwareInvalidChannel);
        }

        // Validate all board IDs exist
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

        // Validate artifact types
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

        // Validate release note types
        foreach (var note in request.ReleaseNotes)
        {
            if (!Enum.TryParse<FirmwareReleaseNoteType>(note.Type, true, out _))
            {
                return Problem(new OpenShockProblem("Firmware.InvalidReleaseNoteType", "The release note type provided is not valid"));
            }
        }

        await using var transaction = await _db.Database.BeginTransactionAsync();

        // Upsert the firmware version
        var existingVersion = await _db.FirmwareVersions.FirstOrDefaultAsync(v => v.Version == firmwareVersion);
        if (existingVersion != null)
        {
            existingVersion.Channel = channel;
            existingVersion.ReleaseDate = request.ReleaseDate;
            existingVersion.CommitHash = request.CommitHash;
            existingVersion.ReleaseUrl = request.ReleaseUrl;
        }
        else
        {
            _db.FirmwareVersions.Add(new FirmwareVersion
            {
                Version = firmwareVersion,
                Channel = channel,
                ReleaseDate = request.ReleaseDate,
                CommitHash = request.CommitHash,
                ReleaseUrl = request.ReleaseUrl
            });
        }
        await _db.SaveChangesAsync();

        // Replace all artifacts for this version
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

    // ---- Board Management ----

    [HttpGet("boards")]
    public async Task<IActionResult> ListBoards()
    {
        var boards = await _db.FirmwareBoards
            .Include(b => b.ChipNavigation)
            .Select(b => new FirmwareBoardDto
            {
                Id = b.Id,
                Name = b.Name,
                ChipId = b.ChipId,
                ChipName = b.ChipNavigation.Name,
                Discontinued = b.Discontinued
            })
            .ToListAsync();

        return Ok(boards);
    }

    [HttpPut("boards/{boardId}")]
    public async Task<IActionResult> UpsertBoard([FromRoute] string boardId, [FromBody] CreateFirmwareBoardRequest request)
    {
        if (!await _db.FirmwareChips.AnyAsync(c => c.Id == request.ChipId))
        {
            return Problem(FirmwareError.FirmwareChipNotFound);
        }

        var existing = await _db.FirmwareBoards.FirstOrDefaultAsync(b => b.Id == boardId);
        if (existing != null)
        {
            existing.Name = request.Name;
            existing.ChipId = request.ChipId;
        }
        else
        {
            _db.FirmwareBoards.Add(new FirmwareBoard
            {
                Id = boardId,
                Name = request.Name,
                ChipId = request.ChipId,
                Discontinued = false
            });
        }

        await _db.SaveChangesAsync();
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

    [HttpGet("chips")]
    public async Task<IActionResult> ListChips()
    {
        var chips = await _db.FirmwareChips
            .Select(c => new FirmwareChipDto
            {
                Id = c.Id,
                Name = c.Name,
                Architecture = c.Architecture != null ? c.Architecture.Value.ToString().ToLowerInvariant() : null
            })
            .ToListAsync();

        return Ok(chips);
    }

    [HttpPut("chips/{chipId}")]
    public async Task<IActionResult> UpsertChip([FromRoute] string chipId, [FromBody] CreateFirmwareChipRequest request)
    {
        FirmwareChipArchitecture? architecture = null;
        if (request.Architecture != null)
        {
            if (!Enum.TryParse<FirmwareChipArchitecture>(request.Architecture, true, out var parsed))
            {
                return Problem(new OpenShockProblem("Firmware.InvalidArchitecture", "The architecture provided is not valid"));
            }
            architecture = parsed;
        }

        var existing = await _db.FirmwareChips.FirstOrDefaultAsync(c => c.Id == chipId);
        if (existing != null)
        {
            existing.Name = request.Name;
            existing.Architecture = architecture;
        }
        else
        {
            _db.FirmwareChips.Add(new FirmwareChip
            {
                Id = chipId,
                Name = request.Name,
                Architecture = architecture
            });
        }

        await _db.SaveChangesAsync();
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
