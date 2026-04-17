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
[Route("/v{version:apiVersion}/firmware/admin/chips")]
[Authorize(AuthenticationSchemes = AuthSchemas.AdminToken)]
public class ChipsAdminController : OpenShockControllerBase
{
    private readonly RepoServerContext _db;

    public ChipsAdminController(RepoServerContext db)
    {
        _db = db;
    }

    [HttpPost]
    public async Task<IActionResult> CreateChip(
        [FromBody] CreateFirmwareChipRequest request,
        CancellationToken ct)
    {
        FirmwareChipArchitecture? architecture = null;
        if (request.Architecture is not null)
        {
            if (!Enum.TryParse<FirmwareChipArchitecture>(request.Architecture, true, out var parsed))
            {
                return Problem(FirmwareError.FirmwareInvalidArchitecture);
            }
            architecture = parsed;
        }

        var chip = new FirmwareChip
        {
            Id = Guid.NewGuid(),
            Name = request.Name,
            Architecture = architecture
        };

        _db.FirmwareChips.Add(chip);
        await _db.SaveChangesAsync(ct);

        return Created((string?)null, new { id = chip.Id });
    }

    [HttpPut("{chipId:guid}")]
    public async Task<IActionResult> UpdateChip(
        [FromRoute] Guid chipId,
        [FromBody] CreateFirmwareChipRequest request,
        CancellationToken ct)
    {
        FirmwareChipArchitecture? architecture = null;
        if (request.Architecture is not null)
        {
            if (!Enum.TryParse<FirmwareChipArchitecture>(request.Architecture, true, out var parsed))
            {
                return Problem(FirmwareError.FirmwareInvalidArchitecture);
            }
            architecture = parsed;
        }

        var chip = await _db.FirmwareChips.FirstOrDefaultAsync(c => c.Id == chipId, ct);
        if (chip is null)
        {
            return Problem(FirmwareError.FirmwareChipNotFound);
        }

        chip.Name = request.Name;
        chip.Architecture = architecture;
        await _db.SaveChangesAsync(ct);

        return Ok();
    }

    [HttpDelete("{chipId:guid}")]
    public async Task<IActionResult> DeleteChip([FromRoute] Guid chipId, CancellationToken ct)
    {
        if (await _db.FirmwareBoards.AnyAsync(b => b.ChipId == chipId, ct))
        {
            return Problem(FirmwareError.FirmwareChipInUse);
        }

        var deleted = await _db.FirmwareChips.Where(c => c.Id == chipId).ExecuteDeleteAsync(ct);
        if (deleted <= 0)
        {
            return Problem(FirmwareError.FirmwareChipNotFound);
        }

        return NoContent();
    }

    [HttpPut("{chipId:guid}/usb-devices/{usbDeviceId:guid}")]
    public async Task<IActionResult> AttachUsbDevice(
        [FromRoute] Guid chipId,
        [FromRoute] Guid usbDeviceId,
        CancellationToken ct)
    {
        if (!await _db.FirmwareChips.AnyAsync(c => c.Id == chipId, ct))
        {
            return Problem(FirmwareError.FirmwareChipNotFound);
        }

        if (!await _db.UsbDevices.AnyAsync(d => d.Id == usbDeviceId, ct))
        {
            return Problem(FirmwareError.FirmwareUsbDeviceNotFound);
        }

        var exists = await _db.FirmwareChipUsbDevices
            .AnyAsync(j => j.ChipId == chipId && j.UsbDeviceId == usbDeviceId, ct);
        if (!exists)
        {
            _db.FirmwareChipUsbDevices.Add(new FirmwareChipUsbDevice
            {
                ChipId = chipId,
                UsbDeviceId = usbDeviceId
            });
            await _db.SaveChangesAsync(ct);
        }

        return NoContent();
    }

    [HttpDelete("{chipId:guid}/usb-devices/{usbDeviceId:guid}")]
    public async Task<IActionResult> DetachUsbDevice(
        [FromRoute] Guid chipId,
        [FromRoute] Guid usbDeviceId,
        CancellationToken ct)
    {
        await _db.FirmwareChipUsbDevices
            .Where(j => j.ChipId == chipId && j.UsbDeviceId == usbDeviceId)
            .ExecuteDeleteAsync(ct);

        return NoContent();
    }
}
