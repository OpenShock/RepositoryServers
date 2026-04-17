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

    [HttpPut("{boardId}")]
    public async Task<IActionResult> UpsertBoard(
        [FromRoute] string boardId,
        [FromBody] CreateFirmwareBoardRequest request,
        CancellationToken ct)
    {
        if (!await _db.FirmwareChips.AnyAsync(c => c.Id == request.ChipId, ct))
        {
            return Problem(FirmwareError.FirmwareChipNotFound);
        }

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

        var existing = await _db.FirmwareBoards.FirstOrDefaultAsync(b => b.Id == boardId, ct);
        if (existing is not null)
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

        await _db.SaveChangesAsync(ct);
        return Created();
    }

    [HttpPatch("{boardId}/discontinue")]
    public async Task<IActionResult> DiscontinueBoard([FromRoute] string boardId, CancellationToken ct)
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

    [HttpDelete("{boardId}")]
    public async Task<IActionResult> DeleteBoard([FromRoute] string boardId, CancellationToken ct)
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

    [HttpPut("{boardId}/usb-devices/{usbDeviceId:guid}")]
    public async Task<IActionResult> AttachUsbDevice(
        [FromRoute] string boardId,
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

    [HttpDelete("{boardId}/usb-devices/{usbDeviceId:guid}")]
    public async Task<IActionResult> DetachUsbDevice(
        [FromRoute] string boardId,
        [FromRoute] Guid usbDeviceId,
        CancellationToken ct)
    {
        await _db.FirmwareBoardUsbDevices
            .Where(j => j.BoardId == boardId && j.UsbDeviceId == usbDeviceId)
            .ExecuteDeleteAsync(ct);

        return NoContent();
    }
}
