using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OpenShock.RepositoryServer.Models.Firmware;
using OpenShock.RepositoryServer.RepoServerDb;

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
    public async Task<IActionResult> ListChips()
    {
        var chips = await _db.FirmwareChips
            .Select(c => new FirmwareChipDto
            {
                Id = c.Id,
                Name = c.Name,
                Architecture = c.Architecture != null ? c.Architecture.Value.ToString().ToLowerInvariant() : null
            })
            .ToListAsync();

        return Ok(chips);
    }
}
