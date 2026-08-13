namespace OpenShock.RepositoryServer.Models.Firmware;

/// <summary>
/// Admin-facing advisory shape — identical to the public shape plus the database id.
/// Returned by the admin CRUD endpoints only.
/// </summary>
public sealed record FirmwareAdvisoryAdminDto
{
    public required Guid Id { get; init; }
    public required string Severity { get; init; }
    public required string Title { get; init; }
    public required string Content { get; init; }
    public required string AffectedVersions { get; init; }
    public string? Url { get; init; }
}
