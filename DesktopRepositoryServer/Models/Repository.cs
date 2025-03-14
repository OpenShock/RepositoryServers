using System.Collections.Immutable;

namespace OpenShock.Desktop.RepositoryServer.Models;

public sealed class Repository
{
    public required string Name { get; init; }
    public required string Id { get; init; }
    public required string Author { get; init; }
    public Uri? Homepage { get; init; } = null;
    
    public required ImmutableDictionary<string, Module> Modules { get; init; }
}