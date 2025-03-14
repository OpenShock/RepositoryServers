namespace OpenShock.Desktop.RepositoryServer.Models;

public class CreateModuleRequest
{
    public required string Name { get; init; }
    public required string Description { get; init; }
    public Uri? SourceUrl { get; init; } = null;
    public Uri? IconUrl { get; init; } = null;
}