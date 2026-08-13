using OpenShock.RepositoryServer.Enums;

namespace OpenShock.RepositoryServer.RepoServerDb.Models;

/// <summary>
/// Security / compatibility advisory shown on the firmware manifest endpoint. Managed
/// via the admin UI at <c>/admin/firmware/advisories</c>. Table: <c>firmware_advisories</c>.
/// </summary>
public sealed class FirmwareAdvisory
{
    public Guid Id { get; set; }
    public required AdvisorySeverity Severity { get; set; }
    public required string Title { get; set; }
    public required string Content { get; set; }

    /// <summary>Semver range string, e.g. <c>"&lt;1.4.0"</c> or <c>"&gt;=1.3.0 &lt;1.4.2"</c>.</summary>
    public required string AffectedVersions { get; set; }

    public string? Url { get; set; }
}
