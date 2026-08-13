using Microsoft.EntityFrameworkCore;
using OneOf;
using OneOf.Types;
using OpenShock.RepositoryServer.RepoServerDb;
using Version = OpenShock.RepositoryServer.RepoServerDb.Version;

namespace OpenShock.RepositoryServer.Services.Admin;

/// <summary>
/// Desktop modules and their published versions.
/// </summary>
/// <remarks>
/// Creating a module is an administrative act, and assigning its owning repository is the
/// authorization decision for publishing to it. A module with no owner is closed to every publisher
/// rather than open to any, so leaving that unset is the safe default for a module not yet wired to
/// its CI.
/// </remarks>
public sealed class ModuleAdminService
{
    private readonly RepoServerContext _db;

    public ModuleAdminService(RepoServerContext db)
    {
        _db = db;
    }

    public Task<Module[]> ListAsync(CancellationToken ct = default) =>
        _db.Modules
            .Include(m => m.Versions)
            .OrderBy(m => m.Id)
            .ToArrayAsync(ct);

    public Task<Module?> FindAsync(string moduleId, CancellationToken ct = default) =>
        _db.Modules.FirstOrDefaultAsync(m => m.Id == moduleId.ToLowerInvariant(), ct);

    public async Task<OneOf<Module, ReferenceNotFound>> UpsertAsync(
        string moduleId, string name, string description, Uri? sourceUrl, Uri? iconUrl,
        Guid? repositoryId, CancellationToken ct = default)
    {
        if (repositoryId is { } id && !await _db.Repositories.AnyAsync(r => r.Id == id, ct))
        {
            return new ReferenceNotFound("repository");
        }

        var normalizedId = moduleId.ToLowerInvariant();
        var module = await _db.Modules.FirstOrDefaultAsync(m => m.Id == normalizedId, ct);

        if (module is null)
        {
            module = new Module { Id = normalizedId };
            _db.Modules.Add(module);
        }

        module.Name = name;
        module.Description = description;
        module.SourceUrl = sourceUrl;
        module.IconUrl = iconUrl;
        module.RepositoryId = repositoryId;

        await _db.SaveChangesAsync(ct);
        return module;
    }

    public async Task<OneOf<Success, NotFound>> DeleteAsync(string moduleId, CancellationToken ct = default)
    {
        var deleted = await _db.Modules
            .Where(m => m.Id == moduleId.ToLowerInvariant())
            .ExecuteDeleteAsync(ct);

        if (deleted <= 0)
        {
            return new NotFound();
        }

        return new Success();
    }

    public Task<Version[]> ListVersionsAsync(string moduleId, CancellationToken ct = default) =>
        _db.Versions
            .Where(v => v.Module == moduleId.ToLowerInvariant())
            .OrderBy(v => v.VersionName)
            .ToArrayAsync(ct);

    /// <summary>
    /// Removes a published module version. The zip stays in storage; only the row that advertises it
    /// is removed, so a client mid-download is not cut off.
    /// </summary>
    public async Task<OneOf<Success, NotFound>> DeleteVersionAsync(
        string moduleId, string moduleVersion, CancellationToken ct = default)
    {
        var normalizedModule = moduleId.ToLowerInvariant();
        var normalizedVersion = moduleVersion.ToLowerInvariant();

        var deleted = await _db.Versions
            .Where(v => v.Module == normalizedModule && v.VersionName == normalizedVersion)
            .ExecuteDeleteAsync(ct);

        if (deleted <= 0)
        {
            return new NotFound();
        }

        return new Success();
    }
}
