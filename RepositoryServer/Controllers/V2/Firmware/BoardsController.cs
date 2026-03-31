using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OpenShock.RepositoryServer.Models.Firmware;
using OpenShock.RepositoryServer.RepoServerDb;

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
    public async Task<IActionResult> ListBoards([FromQuery] string? chipId, [FromQuery] bool includeDiscontinued = false)
    {
        IQueryable<FirmwareBoard> query = _db.FirmwareBoards.Include(b => b.ChipNavigation);

        if (!string.IsNullOrWhiteSpace(chipId))
        {
            query = query.Where(b => b.ChipId == chipId);
        }

        if (!includeDiscontinued)
        {
            query = query.Where(b => !b.Discontinued);
        }

        var boards = await query
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
}
