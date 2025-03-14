namespace OpenShock.Desktop.RepositoryServer.Config;

public sealed class RepoConfig
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Author { get; init; }
    public Uri? Homepage { get; init; } = null;
}