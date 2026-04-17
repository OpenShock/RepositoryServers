using OpenShock.RepositoryServer.Enums;

namespace OpenShock.RepositoryServer.RepoServerDb;

/// <summary>
/// Shared source-code repository reference. Today only firmware versions reference this;
/// desktop modules may adopt the same traceability later. Named <c>SourceRepository</c>
/// to avoid name collision with the desktop manifest DTO <c>Repository</c>.
/// Table: <c>repositories</c>. Rows are created automatically when an OIDC token from
/// a new owner/repo pair is accepted — there is no manual upsert endpoint.
/// </summary>
public class SourceRepository
{
    public Guid Id { get; set; }
    public RepositoryProvider Provider { get; set; }
    public string Owner { get; set; } = null!;
    public string Repo { get; set; } = null!;

    public virtual ICollection<FirmwareVersion> FirmwareVersions { get; set; } = new List<FirmwareVersion>();
    public virtual ICollection<FirmwareRelease> FirmwareReleases { get; set; } = new List<FirmwareRelease>();
}
