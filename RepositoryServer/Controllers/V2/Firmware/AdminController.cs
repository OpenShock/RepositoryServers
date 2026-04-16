using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OpenShock.RepositoryServer.Enums;
using OpenShock.RepositoryServer.Models.Firmware;
using OpenShock.RepositoryServer.Problems;
using OpenShock.RepositoryServer.RepoServerDb;

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

    [HttpDelete("versions/{firmwareVersion}")]
    public async Task<IActionResult> DeleteVersion([FromRoute] string firmwareVersion)
    {
        var deleted = await _db.FirmwareVersions.Where(v => v.Version == firmwareVersion).ExecuteDeleteAsync();
        if (deleted <= 0) return Problem(FirmwareError.FirmwareVersionNotFound);
        return Ok();
    }

    // ---- Board Management ----

    [HttpPut("boards/{boardId}")]
    public async Task<IActionResult> UpsertBoard([FromRoute] string boardId, [FromBody] CreateFirmwareBoardRequest request)
    {
        if (!await _db.FirmwareChips.AnyAsync(c => c.Id == request.ChipId))
        {
            return Problem(FirmwareError.FirmwareChipNotFound);
        }

        // Parse and validate required artifact types
        var requiredArtifactTypes = Array.Empty<FirmwareArtifactType>();
        if (request.RequiredArtifactTypes is { Count: > 0 })
        {
            var parsed = new List<FirmwareArtifactType>();
            foreach (var typeStr in request.RequiredArtifactTypes)
            {
                if (!Enum.TryParse<FirmwareArtifactType>(typeStr, true, out var artifactType))
                {
                    return Problem(FirmwareError.FirmwareInvalidArtifactType);
                }
                parsed.Add(artifactType);
            }
            requiredArtifactTypes = parsed.Distinct().ToArray();
        }

        var existing = await _db.FirmwareBoards.FirstOrDefaultAsync(b => b.Id == boardId);
        if (existing != null)
        {
            existing.Name = request.Name;
            existing.ChipId = request.ChipId;
            existing.RequiredArtifactTypes = requiredArtifactTypes;
        }
        else
        {
            _db.FirmwareBoards.Add(new FirmwareBoard
            {
                Id = boardId,
                Name = request.Name,
                ChipId = request.ChipId,
                Discontinued = false,
                RequiredArtifactTypes = requiredArtifactTypes
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
