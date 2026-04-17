using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OpenShock.RepositoryServer.Enums;
using OpenShock.RepositoryServer.Models.Firmware;
using OpenShock.RepositoryServer.Problems;
using OpenShock.RepositoryServer.RepoServerDb;

namespace OpenShock.RepositoryServer.Controllers.V2.Firmware.Admin;

[ApiVersion("2.0")]
[ApiController]
[Route("/v{version:apiVersion}/firmware/admin/boards")]
[Authorize(AuthenticationSchemes = AuthSchemas.AdminToken)]
public class BoardsAdminController : OpenShockControllerBase
{
    private readonly RepoServerContext _db;

    public BoardsAdminController(RepoServerContext db)
    {
        _db = db;
    }

    /// <summary>
    /// Create a new board. <c>Name</c> is the unique identifier visible to clients.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> CreateBoard(
        [FromBody] CreateFirmwareBoardRequest request,
        CancellationToken ct)
    {
        if (!await _db.FirmwareChips.AnyAsync(c => c.Id == request.ChipId, ct))
        {
            return Problem(FirmwareError.FirmwareChipNotFound);
        }

        if (!TryParseRequiredArtifactTypes(request.RequiredArtifactTypes, out var requiredArtifactTypes))
        {
            return Problem(FirmwareError.FirmwareInvalidArtifactType);
        }

        var board = new FirmwareBoard
        {
            Id = Guid.NewGuid(),
            Name = request.Name,
            ChipId = request.ChipId,
            Discontinued = false,
            RequiredArtifactTypes = requiredArtifactTypes
        };

        _db.FirmwareBoards.Add(board);
        await _db.SaveChangesAsync(ct);

        return Created((string?)null, new { id = board.Id });
    }

    [HttpPut("{boardId:guid}")]
    public async Task<IActionResult> UpdateBoard(
        [FromRoute] Guid boardId,
        [FromBody] CreateFirmwareBoardRequest request,
        CancellationToken ct)
    {
        if (!await _db.FirmwareChips.AnyAsync(c => c.Id == request.ChipId, ct))
        {
            return Problem(FirmwareError.FirmwareChipNotFound);
        }

        if (!TryParseRequiredArtifactTypes(request.RequiredArtifactTypes, out var requiredArtifactTypes))
        {
            return Problem(FirmwareError.FirmwareInvalidArtifactType);
        }

        var board = await _db.FirmwareBoards.FirstOrDefaultAsync(b => b.Id == boardId, ct);
        if (board is null)
        {
            return Problem(FirmwareError.FirmwareBoardNotFound);
        }

        board.Name = request.Name;
        board.ChipId = request.ChipId;
        board.RequiredArtifactTypes = requiredArtifactTypes;

        await _db.SaveChangesAsync(ct);
        return Ok();
    }

    [HttpPatch("{boardId:guid}/discontinue")]
    public async Task<IActionResult> DiscontinueBoard([FromRoute] Guid boardId, CancellationToken ct)
    {
        var board = await _db.FirmwareBoards.FirstOrDefaultAsync(b => b.Id == boardId, ct);
        if (board is null)
        {
            return Problem(FirmwareError.FirmwareBoardNotFound);
        }

        board.Discontinued = true;
        await _db.SaveChangesAsync(ct);
        return Ok();
    }

    [HttpDelete("{boardId:guid}")]
    public async Task<IActionResult> DeleteBoard([FromRoute] Guid boardId, CancellationToken ct)
    {
        if (await _db.FirmwareArtifacts.AnyAsync(a => a.BoardId == boardId, ct))
        {
            return Problem(FirmwareError.FirmwareBoardInUse);
        }

        var deleted = await _db.FirmwareBoards.Where(b => b.Id == boardId).ExecuteDeleteAsync(ct);
        if (deleted <= 0)
        {
            return Problem(FirmwareError.FirmwareBoardNotFound);
        }

        return NoContent();
    }

    [HttpPut("{boardId:guid}/usb-devices/{usbDeviceId:guid}")]
    public async Task<IActionResult> AttachUsbDevice(
        [FromRoute] Guid boardId,
        [FromRoute] Guid usbDeviceId,
        CancellationToken ct)
    {
        if (!await _db.FirmwareBoards.AnyAsync(b => b.Id == boardId, ct))
        {
            return Problem(FirmwareError.FirmwareBoardNotFound);
        }

        if (!await _db.UsbDevices.AnyAsync(d => d.Id == usbDeviceId, ct))
        {
            return Problem(FirmwareError.FirmwareUsbDeviceNotFound);
        }

        var exists = await _db.FirmwareBoardUsbDevices
            .AnyAsync(j => j.BoardId == boardId && j.UsbDeviceId == usbDeviceId, ct);
        if (!exists)
        {
            _db.FirmwareBoardUsbDevices.Add(new FirmwareBoardUsbDevice
            {
                BoardId = boardId,
                UsbDeviceId = usbDeviceId
            });
            await _db.SaveChangesAsync(ct);
        }

        return NoContent();
    }

    [HttpDelete("{boardId:guid}/usb-devices/{usbDeviceId:guid}")]
    public async Task<IActionResult> DetachUsbDevice(
        [FromRoute] Guid boardId,
        [FromRoute] Guid usbDeviceId,
        CancellationToken ct)
    {
        await _db.FirmwareBoardUsbDevices
            .Where(j => j.BoardId == boardId && j.UsbDeviceId == usbDeviceId)
            .ExecuteDeleteAsync(ct);

        return NoContent();
    }

    private static bool TryParseRequiredArtifactTypes(List<string>? raw, out FirmwareArtifactType[] parsed)
    {
        if (raw is null || raw.Count == 0)
        {
            parsed = Array.Empty<FirmwareArtifactType>();
            return true;
        }

        var list = new List<FirmwareArtifactType>(raw.Count);
        foreach (var typeStr in raw)
        {
            if (!Enum.TryParse<FirmwareArtifactType>(typeStr, true, out var t))
            {
                parsed = Array.Empty<FirmwareArtifactType>();
                return false;
            }
            list.Add(t);
        }

        parsed = list.Distinct().ToArray();
        return true;
    }
}
