using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OpenShock.RepositoryServer.Models.Firmware;
using OpenShock.RepositoryServer.Problems;
using OpenShock.RepositoryServer.RepoServerDb;

namespace OpenShock.RepositoryServer.Controllers.V2.Firmware.Admin;

[ApiVersion("2.0")]
[ApiController]
[Route("/v{version:apiVersion}/firmware/admin/usb-devices")]
[Authorize(AuthenticationSchemes = AuthSchemas.AdminToken)]
public class UsbDevicesController : OpenShockControllerBase
{
    private readonly RepoServerContext _db;

    public UsbDevicesController(RepoServerContext db)
    {
        _db = db;
    }

    [HttpPut]
    public async Task<IActionResult> UpsertUsbDevice([FromBody] UpsertUsbDeviceRequest request, CancellationToken ct)
    {
        var existing = await _db.UsbDevices
            .FirstOrDefaultAsync(d => d.Vid == request.Vid && d.Pid == request.Pid, ct);

        if (existing is not null)
        {
            existing.Name = request.Name;
            await _db.SaveChangesAsync(ct);
            return Ok(ToDto(existing));
        }

        var device = new UsbDevice
        {
            Id = Guid.NewGuid(),
            Vid = request.Vid,
            Pid = request.Pid,
            Name = request.Name
        };
        _db.UsbDevices.Add(device);
        await _db.SaveChangesAsync(ct);

        return Created((string?)null, ToDto(device));
    }

    [HttpGet]
    public async Task<IActionResult> ListUsbDevices(CancellationToken ct)
    {
        var rows = await _db.UsbDevices
            .OrderBy(d => d.Vid).ThenBy(d => d.Pid)
            .Select(d => new FirmwareUsbDeviceDto
            {
                Id = d.Id,
                Vid = d.Vid,
                Pid = d.Pid,
                Name = d.Name
            })
            .ToListAsync(ct);

        return Ok(rows);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteUsbDevice([FromRoute] Guid id, CancellationToken ct)
    {
        var inUse =
            await _db.FirmwareChipUsbDevices.AnyAsync(j => j.UsbDeviceId == id, ct) ||
            await _db.FirmwareBoardUsbDevices.AnyAsync(j => j.UsbDeviceId == id, ct);

        if (inUse)
        {
            return Problem(FirmwareError.FirmwareUsbDeviceInUse);
        }

        var deleted = await _db.UsbDevices.Where(d => d.Id == id).ExecuteDeleteAsync(ct);
        if (deleted <= 0)
        {
            return Problem(FirmwareError.FirmwareUsbDeviceNotFound);
        }

        return NoContent();
    }

    private static FirmwareUsbDeviceDto ToDto(UsbDevice d) => new()
    {
        Id = d.Id,
        Vid = d.Vid,
        Pid = d.Pid,
        Name = d.Name
    };
}
