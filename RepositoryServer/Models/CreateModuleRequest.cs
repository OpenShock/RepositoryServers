namespace OpenShock.RepositoryServer.Models;

public class CreateModuleRequest
{
    public required string Name { get; init; }
    public required string Description { get; init; }
    public Uri? SourceUrl { get; init; } = null;
    public Uri? IconUrl { get; init; } = null;

    /// <summary>
    /// Repository authorized to publish versions of this module, from the <c>repositories</c> table.
    /// Leave null to leave the module closed to all publishers — CI/CD uploads are rejected until an
    /// owner is assigned.
    /// </summary>
    public Guid? RepositoryId { get; init; } = null;
}