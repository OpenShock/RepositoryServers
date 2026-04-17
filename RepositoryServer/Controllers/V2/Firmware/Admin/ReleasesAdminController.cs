using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OpenShock.RepositoryServer.Enums;
using OpenShock.RepositoryServer.Models.Firmware;
using OpenShock.RepositoryServer.Problems;
using OpenShock.RepositoryServer.RepoServerDb;
using OpenShock.RepositoryServer.Utils;

namespace OpenShock.RepositoryServer.Controllers.V2.Firmware.Admin;

[ApiVersion("2.0")]
[ApiController]
[Route("/v{version:apiVersion}/firmware/admin/releases")]
[Authorize(AuthenticationSchemes = AuthSchemas.AdminToken)]
public class ReleasesAdminController : OpenShockControllerBase
{
    private readonly RepoServerContext _db;

    public ReleasesAdminController(RepoServerContext db)
    {
        _db = db;
    }

    [HttpPut("{releaseId:guid}/changelog")]
    public async Task<IActionResult> FixChangelog(
        [FromRoute] Guid releaseId,
        [FromBody] FixChangelogRequest request,
        CancellationToken ct)
    {
        var release = await _db.FirmwareReleases.FirstOrDefaultAsync(r => r.Id == releaseId, ct);
        if (release is null)
        {
            return Problem(FirmwareError.FirmwareReleaseNotFound);
        }

        if (release.Status != ReleaseStatus.Staging && release.Status != ReleaseStatus.Editing)
        {
            return Problem(FirmwareError.FirmwareReleaseNotEditable);
        }

        var parseResult = ChangelogParser.Parse(request.Changelog);
        if (!parseResult.TryPickT0(out var notes, out var error))
        {
            return Problem(FirmwareError.FirmwareInvalidChangelog(error switch
            {
                ChangelogParseError.Empty => "Changelog is empty or whitespace-only",
                ChangelogParseError.NoHeadings => "Changelog contains no '### Heading' sections",
                ChangelogParseError.AllSectionsEmpty => "All changelog sections are empty",
                _ => error.ToString()
            }));
        }

        await _db.FirmwareStagedReleaseNotes
            .Where(n => n.ReleaseId == releaseId)
            .ExecuteDeleteAsync(ct);

        for (var i = 0; i < notes.Count; i++)
        {
            var n = notes[i];
            _db.FirmwareStagedReleaseNotes.Add(new FirmwareStagedReleaseNote
            {
                ReleaseId = releaseId,
                Index = i,
                SectionType = Enum.Parse<ReleaseNoteSectionType>(n.Type, true),
                Title = n.Title,
                Content = n.Content,
            });
        }

        release.Status = ReleaseStatus.Staging;
        await _db.SaveChangesAsync(ct);

        return Ok();
    }
}
