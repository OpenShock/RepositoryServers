using System.ComponentModel.DataAnnotations;

namespace OpenShock.RepositoryServer.Config;

public sealed class FirmwareConfig
{
    [Required(AllowEmptyStrings = false)] public required string CdnBaseUrl { get; init; }
    [Required] public required StorageConfig Storage { get; init; }

    /// <summary>
    /// How long a release may remain in <c>staging</c> status before the cleanup job aborts it.
    /// </summary>
    public TimeSpan StagedReleaseTtl { get; init; } = TimeSpan.FromHours(1);

    /// <summary>
    /// How long a release may remain in <c>editing</c> status before the cleanup job aborts it.
    /// </summary>
    public TimeSpan EditingReleaseTtl { get; init; } = TimeSpan.FromDays(7);

    /// <summary>
    /// Whether publish checks that each artifact is retrievable at the URL it will advertise,
    /// refusing to publish when it is not.
    /// </summary>
    /// <remarks>
    /// On by default: a release whose artifacts 404 is indistinguishable from a healthy one through
    /// the API, and the client that finds out is a hub mid-update.
    ///
    /// Turn it off only where the server genuinely cannot reach its own CDN hostname — split-horizon
    /// DNS, egress restrictions. Doing so does not make the release good, it only stops the server
    /// from being able to tell.
    /// </remarks>
    public bool VerifyPublishedArtifacts { get; init; } = true;
}
