namespace OpenShock.RepositoryServer.Models.Firmware;

/// <summary>
/// Security / compatibility advisory exposed on the manifest endpoint. Served from the
/// <c>firmware_advisories</c> table; managed via <c>/v2/firmware/admin/advisories</c>.
/// </summary>
public sealed record FirmwareAdvisoryDto
{
    public required string Severity { get; init; }
    public required string Title { get; init; }
    public required string Content { get; init; }
    public required string AffectedVersions { get; init; }
    public string? Url { get; init; }
}
