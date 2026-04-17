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
[Route("/v{version:apiVersion}/firmware/admin/usb-serial-filters")]
[Authorize(AuthenticationSchemes = AuthSchemas.AdminToken)]
public class UsbSerialFiltersController : OpenShockControllerBase
{
    private readonly RepoServerContext _db;

    public UsbSerialFiltersController(RepoServerContext db)
    {
        _db = db;
    }

    [HttpPut]
    public async Task<IActionResult> UpsertFilter([FromBody] UpsertUsbSerialFilterRequest request, CancellationToken ct)
    {
        // Unique (vid, pid) with NULLS NOT DISTINCT — at most one vendor-wide row per VID.
        var existing = await _db.UsbSerialFilters
            .FirstOrDefaultAsync(f => f.Vid == request.Vid && f.Pid == request.Pid, ct);

        if (existing is not null)
        {
            existing.Description = request.Description;
            await _db.SaveChangesAsync(ct);
            return Ok(ToDto(existing));
        }

        var filter = new UsbSerialFilter
        {
            Id = Guid.NewGuid(),
            Vid = request.Vid,
            Pid = request.Pid,
            Description = request.Description
        };
        _db.UsbSerialFilters.Add(filter);
        await _db.SaveChangesAsync(ct);

        return Created((string?)null, ToDto(filter));
    }

    [HttpGet]
    public async Task<IActionResult> ListFilters(CancellationToken ct)
    {
        var rows = await _db.UsbSerialFilters
            .OrderBy(f => f.Vid).ThenBy(f => f.Pid)
            .Select(f => new FirmwareUsbSerialFilterAdminDto
            {
                Id = f.Id,
                Vid = f.Vid,
                Pid = f.Pid,
                Description = f.Description
            })
            .ToListAsync(ct);

        return Ok(rows);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteFilter([FromRoute] Guid id, CancellationToken ct)
    {
        var deleted = await _db.UsbSerialFilters.Where(f => f.Id == id).ExecuteDeleteAsync(ct);
        if (deleted <= 0)
        {
            return Problem(FirmwareError.FirmwareUsbSerialFilterNotFound);
        }

        return NoContent();
    }

    private static FirmwareUsbSerialFilterAdminDto ToDto(UsbSerialFilter f) => new()
    {
        Id = f.Id,
        Vid = f.Vid,
        Pid = f.Pid,
        Description = f.Description
    };
}
