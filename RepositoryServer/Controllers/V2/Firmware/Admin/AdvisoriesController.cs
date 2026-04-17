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
[Route("/v{version:apiVersion}/firmware/admin/advisories")]
[Authorize(AuthenticationSchemes = AuthSchemas.AdminToken)]
public class AdvisoriesController : OpenShockControllerBase
{
    private readonly RepoServerContext _db;

    public AdvisoriesController(RepoServerContext db)
    {
        _db = db;
    }

    [HttpPost]
    public async Task<IActionResult> CreateAdvisory([FromBody] UpsertFirmwareAdvisoryRequest request, CancellationToken ct)
    {
        if (!TryParseSeverity(request.Severity, out var severity))
        {
            return Problem(FirmwareError.FirmwareInvalidAdvisorySeverity);
        }

        var advisory = new FirmwareAdvisory
        {
            Id = Guid.NewGuid(),
            Severity = severity,
            Title = request.Title,
            Content = request.Content,
            AffectedVersions = request.AffectedVersions,
            Url = request.Url
        };
        _db.FirmwareAdvisories.Add(advisory);
        await _db.SaveChangesAsync(ct);

        return Created((string?)null, ToDto(advisory));
    }

    [HttpGet]
    public async Task<IActionResult> ListAdvisories(CancellationToken ct)
    {
        var rows = await _db.FirmwareAdvisories
            .OrderBy(a => a.Severity)
            .ThenBy(a => a.Title)
            .ToListAsync(ct);

        return Ok(rows.Select(ToDto));
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> UpdateAdvisory(
        [FromRoute] Guid id,
        [FromBody] UpsertFirmwareAdvisoryRequest request,
        CancellationToken ct)
    {
        if (!TryParseSeverity(request.Severity, out var severity))
        {
            return Problem(FirmwareError.FirmwareInvalidAdvisorySeverity);
        }

        var advisory = await _db.FirmwareAdvisories.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (advisory is null)
        {
            return Problem(FirmwareError.FirmwareAdvisoryNotFound);
        }

        advisory.Severity = severity;
        advisory.Title = request.Title;
        advisory.Content = request.Content;
        advisory.AffectedVersions = request.AffectedVersions;
        advisory.Url = request.Url;
        await _db.SaveChangesAsync(ct);

        return Ok(ToDto(advisory));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteAdvisory([FromRoute] Guid id, CancellationToken ct)
    {
        var deleted = await _db.FirmwareAdvisories.Where(a => a.Id == id).ExecuteDeleteAsync(ct);
        if (deleted <= 0)
        {
            return Problem(FirmwareError.FirmwareAdvisoryNotFound);
        }

        return NoContent();
    }

    private static bool TryParseSeverity(string value, out AdvisorySeverity severity) =>
        Enum.TryParse(value, true, out severity);

    private static FirmwareAdvisoryAdminDto ToDto(FirmwareAdvisory a) => new()
    {
        Id = a.Id,
        Severity = a.Severity.ToString().ToLowerInvariant(),
        Title = a.Title,
        Content = a.Content,
        AffectedVersions = a.AffectedVersions,
        Url = a.Url
    };
}
