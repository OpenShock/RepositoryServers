using Asp.Versioning;
using FlexLabs.EntityFrameworkCore.Upsert;
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

    [HttpPut("{chipId}")]
    public async Task<IActionResult> UpsertChip(
        [FromRoute] string chipId,
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
            Id = chipId,
            Name = request.Name,
            Architecture = architecture
        };

        var executed = await _db.FirmwareChips.Upsert(chip).On(c => c.Id).RunAsync(ct);
        if (executed <= 0) throw new Exception("Failed to upsert firmware chip");

        return Created();
    }

    [HttpDelete("{chipId}")]
    public async Task<IActionResult> DeleteChip([FromRoute] string chipId, CancellationToken ct)
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

    [HttpPut("{chipId}/usb-devices/{usbDeviceId:guid}")]
    public async Task<IActionResult> AttachUsbDevice(
        [FromRoute] string chipId,
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

    [HttpDelete("{chipId}/usb-devices/{usbDeviceId:guid}")]
    public async Task<IActionResult> DetachUsbDevice(
        [FromRoute] string chipId,
        [FromRoute] Guid usbDeviceId,
        CancellationToken ct)
    {
        await _db.FirmwareChipUsbDevices
            .Where(j => j.ChipId == chipId && j.UsbDeviceId == usbDeviceId)
            .ExecuteDeleteAsync(ct);

        return NoContent();
    }
}
