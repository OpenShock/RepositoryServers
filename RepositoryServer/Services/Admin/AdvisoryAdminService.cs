using Microsoft.EntityFrameworkCore;
using OneOf;
using OneOf.Types;
using OpenShock.RepositoryServer.Enums;
using OpenShock.RepositoryServer.RepoServerDb;
using OpenShock.RepositoryServer.RepoServerDb.Models;

namespace OpenShock.RepositoryServer.Services.Admin;

/// <summary>
/// Security and compatibility advisories surfaced on the public firmware manifest.
/// </summary>
public sealed class AdvisoryAdminService
{
    private readonly RepoServerContext _db;

    public AdvisoryAdminService(RepoServerContext db)
    {
        _db = db;
    }

    public async Task<FirmwareAdvisory[]> ListAsync(CancellationToken ct = default)
    {
        // Postgres orders enum columns by label creation order, which with MapEnum is alphabetical and
        // has nothing to do with severity rank. Ordering happens in memory on the CLR enum instead.
        var rows = await _db.FirmwareAdvisories.ToListAsync(ct);

        return rows
            .OrderBy(a => a.Severity)
            .ThenBy(a => a.Title)
            .ToArray();
    }

    public Task<FirmwareAdvisory?> FindAsync(Guid id, CancellationToken ct = default) =>
        _db.FirmwareAdvisories.FirstOrDefaultAsync(a => a.Id == id, ct);

    public async Task<FirmwareAdvisory> CreateAsync(
        AdvisorySeverity severity, string title, string content, string affectedVersions, string? url,
        CancellationToken ct = default)
    {
        var advisory = new FirmwareAdvisory
        {
            Id = Guid.NewGuid(),
            Severity = severity,
            Title = title,
            Content = content,
            AffectedVersions = affectedVersions,
            Url = url
        };

        _db.FirmwareAdvisories.Add(advisory);
        await _db.SaveChangesAsync(ct);

        return advisory;
    }

    public async Task<OneOf<Success, NotFound>> UpdateAsync(
        Guid id, AdvisorySeverity severity, string title, string content, string affectedVersions,
        string? url, CancellationToken ct = default)
    {
        var advisory = await _db.FirmwareAdvisories.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (advisory is null)
        {
            return new NotFound();
        }

        advisory.Severity = severity;
        advisory.Title = title;
        advisory.Content = content;
        advisory.AffectedVersions = affectedVersions;
        advisory.Url = url;
        await _db.SaveChangesAsync(ct);

        return new Success();
    }

    public async Task<OneOf<Success, NotFound>> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var deleted = await _db.FirmwareAdvisories.Where(a => a.Id == id).ExecuteDeleteAsync(ct);
        if (deleted <= 0)
        {
            return new NotFound();
        }

        return new Success();
    }
}
