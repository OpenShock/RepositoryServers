namespace OpenShock.RepositoryServer.Models.Firmware;

/// <summary>
/// Security / compatibility advisory exposed on the manifest endpoint. Served from the
/// <c>firmware_advisories</c> table; managed in the admin UI at <c>/admin/firmware/advisories</c>.
/// </summary>
public sealed record FirmwareAdvisoryDto
{
    public required string Severity { get; init; }
    public required string Title { get; init; }
    public required string Content { get; init; }
    public required string AffectedVersions { get; init; }
    public string? Url { get; init; }
}
