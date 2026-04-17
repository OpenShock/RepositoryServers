using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OpenShock.RepositoryServer.Models.Firmware;
using OpenShock.RepositoryServer.RepoServerDb;
using OpenShock.RepositoryServer.Utils;

namespace OpenShock.RepositoryServer.Controllers.V2.Firmware;

[ApiVersion("2.0")]
[ApiController]
[Route("/v{version:apiVersion}/firmware/boards")]
public sealed class BoardsController : OpenShockControllerBase
{
    private readonly RepoServerContext _db;

    public BoardsController(RepoServerContext db)
    {
        _db = db;
    }

    [HttpGet]
    [CacheControl(300)]
    public async Task<IActionResult> ListBoards(
        [FromQuery] Guid? chipId,
        [FromQuery] bool includeDiscontinued = true,
        CancellationToken ct = default)
    {
        IQueryable<FirmwareBoard> query = _db.FirmwareBoards
            .Include(b => b.ChipNavigation)
            .Include(b => b.UsbDevices);

        if (chipId is { } cid)
        {
            query = query.Where(b => b.ChipId == cid);
        }

        if (!includeDiscontinued)
        {
            query = query.Where(b => !b.Discontinued);
        }

        var rows = await query.OrderBy(b => b.Name).ToListAsync(ct);

        var boards = rows
            .Select(b => new FirmwareBoardDto
            {
                Id = b.Id,
                Name = b.Name,
                ChipId = b.ChipId,
                ChipName = b.ChipNavigation.Name,
                Discontinued = b.Discontinued,
                UsbDevices = b.UsbDevices
                    .Select(d => new FirmwareUsbDeviceDto { Id = d.Id, Vid = d.Vid, Pid = d.Pid, Name = d.Name })
                    .ToList()
            })
            .ToList();

        return Ok(boards);
    }
}
