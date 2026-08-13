using System.ComponentModel.DataAnnotations;

namespace OpenShock.RepositoryServer.Models.Firmware;

public sealed class UpsertFirmwareAdvisoryRequest
{
    /// <summary>One of <c>critical</c>, <c>warning</c>, <c>info</c>.</summary>
    [Required(AllowEmptyStrings = false)]
    public required string Severity { get; init; }

    [Required(AllowEmptyStrings = false)]
    [MaxLength(256)]
    public required string Title { get; init; }

    [Required(AllowEmptyStrings = false)]
    public required string Content { get; init; }

    /// <summary>Semver range string, e.g. <c>"&lt;1.4.0"</c> or <c>"&gt;=1.3.0 &lt;1.4.2"</c>.</summary>
    [Required(AllowEmptyStrings = false)]
    [MaxLength(128)]
    public required string AffectedVersions { get; init; }

    [MaxLength(512)]
    public string? Url { get; init; }
}
