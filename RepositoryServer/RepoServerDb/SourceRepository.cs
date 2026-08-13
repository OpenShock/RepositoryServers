using OpenShock.RepositoryServer.Enums;

namespace OpenShock.RepositoryServer.RepoServerDb;

/// <summary>
/// Shared source-code repository reference. Today only firmware versions reference this;
/// desktop modules may adopt the same traceability later. Named <c>SourceRepository</c>
/// to avoid name collision with the desktop manifest DTO <c>Repository</c>.
/// Table: <c>repositories</c>. Doubles as the publish allowlist: rows are created only by an
/// administrator, and an OIDC token from an unregistered owner/repo pair is rejected.
/// </summary>
public class SourceRepository
{
    public Guid Id { get; set; }
    public RepositoryProvider Provider { get; set; }
    public string Owner { get; set; } = null!;
    public string Repo { get; set; } = null!;

    /// <summary>
    /// What this repository may publish. Empty means it is registered but cannot publish anything,
    /// which is the safe default for a newly added row.
    /// </summary>
    public RepositoryScope[] Scopes { get; set; } = [];

    public virtual ICollection<FirmwareVersion> FirmwareVersions { get; set; } = new List<FirmwareVersion>();
    public virtual ICollection<FirmwareRelease> FirmwareReleases { get; set; } = new List<FirmwareRelease>();
}
