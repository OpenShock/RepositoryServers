namespace OpenShock.RepositoryServer.Models.Firmware;

/// <summary>
/// Paginated list response for <c>GET /v2/firmware/versions</c>.
/// <see cref="Total"/> is the unfiltered count before limit/offset.
/// </summary>
public sealed record VersionListResponse
{
    public required List<FirmwareVersionSummary> Versions { get; init; }
    public required int Total { get; init; }
}
