using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OpenShock.RepositoryServer.Models.Firmware;
using OpenShock.RepositoryServer.RepoServerDb;
using OpenShock.RepositoryServer.Utils;

namespace OpenShock.RepositoryServer.Controllers.V2.Firmware;

[ApiVersion("2.0")]
[ApiController]
[Route("/v{version:apiVersion}/firmware/chips")]
public sealed class ChipsController : OpenShockControllerBase
{
    private readonly RepoServerContext _db;

    public ChipsController(RepoServerContext db)
    {
        _db = db;
    }

    [HttpGet]
    [CacheControl(300)]
    public async Task<IActionResult> ListChips(CancellationToken ct)
    {
        var rows = await _db.FirmwareChips
            .Include(c => c.UsbDevices)
            .OrderBy(c => c.Name)
            .ToListAsync(ct);

        var chips = rows
            .Select(c => new FirmwareChipDto
            {
                Id = c.Id,
                Name = c.Name,
                Architecture = c.Architecture?.ToString().ToLowerInvariant(),
                UsbDevices = c.UsbDevices
                    .Select(d => new FirmwareUsbDeviceDto { Id = d.Id, Vid = d.Vid, Pid = d.Pid, Name = d.Name })
                    .ToList()
            })
            .ToList();

        return Ok(chips);
    }
}
