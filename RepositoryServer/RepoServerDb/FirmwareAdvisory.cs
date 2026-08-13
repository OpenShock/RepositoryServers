using OpenShock.RepositoryServer.Enums;

namespace OpenShock.RepositoryServer.RepoServerDb;

/// <summary>
/// Security / compatibility advisory shown on the firmware manifest endpoint. Managed
/// via <c>/v2/firmware/admin/advisories</c>. Table: <c>firmware_advisories</c>.
/// </summary>
public class FirmwareAdvisory
{
    public Guid Id { get; set; }
    public AdvisorySeverity Severity { get; set; }
    public string Title { get; set; } = null!;
    public string Content { get; set; } = null!;

    /// <summary>Semver range string, e.g. <c>"&lt;1.4.0"</c> or <c>"&gt;=1.3.0 &lt;1.4.2"</c>.</summary>
    public string AffectedVersions { get; set; } = null!;

    public string? Url { get; set; }
}
