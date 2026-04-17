using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OpenShock.RepositoryServer.Enums;
using OpenShock.RepositoryServer.Models.Firmware;
using OpenShock.RepositoryServer.RepoServerDb;
using OpenShock.RepositoryServer.Utils;

namespace OpenShock.RepositoryServer.Controllers.V2.Firmware;

[ApiVersion("2.0")]
[ApiController]
[Route("/v{version:apiVersion}/firmware/manifest")]
public sealed class ManifestController : OpenShockControllerBase
{
    private static readonly ReleaseChannel[] AllChannels =
    [
        ReleaseChannel.Stable,
        ReleaseChannel.Beta,
        ReleaseChannel.Develop
    ];

    private readonly RepoServerContext _db;

    public ManifestController(RepoServerContext db)
    {
        _db = db;
    }

    [HttpGet]
    [CacheControl(300)]
    public async Task<IActionResult> GetManifest(CancellationToken ct)
    {
        // DbContext is not thread-safe; queries run sequentially. The manifest is cached
        // at the HTTP layer (max-age=300) so serialization here is a non-issue.
        var latest = new Dictionary<string, string>();
        foreach (var ch in AllChannels)
        {
            var version = await _db.FirmwareVersions
                .Where(v => v.Channel == ch)
                .OrderByDescending(v => v.ReleaseDate)
                .Select(v => v.Version)
                .FirstOrDefaultAsync(ct);

            if (version is not null)
            {
                latest[ch.ToString().ToLowerInvariant()] = version;
            }
        }

        var boardRows = await _db.FirmwareBoards
            .Include(b => b.ChipNavigation)
            .Include(b => b.UsbDevices)
            .OrderBy(b => b.Name)
            .ToListAsync(ct);

        var chipRows = await _db.FirmwareChips
            .Include(c => c.UsbDevices)
            .OrderBy(c => c.Name)
            .ToListAsync(ct);

        var filterRows = await _db.UsbSerialFilters
            .OrderBy(f => f.Vid).ThenBy(f => f.Pid)
            .Select(f => new FirmwareUsbSerialFilterDto { Vid = f.Vid, Pid = f.Pid })
            .ToListAsync(ct);

        var deviceRows = await _db.UsbDevices
            .OrderBy(d => d.Vid).ThenBy(d => d.Pid)
            .Select(d => new FirmwareUsbDeviceDto
            {
                Id = d.Id,
                Vid = d.Vid,
                Pid = d.Pid,
                Name = d.Name
            })
            .ToListAsync(ct);

        var boards = boardRows
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

        var chips = chipRows
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

        var advisories = await _db.FirmwareAdvisories
            .OrderBy(a => a.Severity)
            .ThenBy(a => a.Title)
            .Select(a => new FirmwareAdvisoryDto
            {
                Severity = a.Severity.ToString().ToLowerInvariant(),
                Title = a.Title,
                Content = a.Content,
                AffectedVersions = a.AffectedVersions,
                Url = a.Url
            })
            .ToListAsync(ct);

        var response = new FirmwareManifestResponse
        {
            Channels = AllChannels.Select(c => c.ToString().ToLowerInvariant()).ToList(),
            Latest = latest,
            Boards = boards,
            Chips = chips,
            UsbSerialFilters = filterRows,
            UsbDevices = deviceRows,
            Advisories = advisories
        };

        return Ok(response);
    }
}
