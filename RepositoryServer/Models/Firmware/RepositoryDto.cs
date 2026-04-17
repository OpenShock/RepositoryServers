using OpenShock.RepositoryServer.Enums;

namespace OpenShock.RepositoryServer.Models.Firmware;

/// <summary>
/// Reference to a source code repository. Shared infrastructure for firmware version
/// traceability. Rows are auto-created from incoming CI/CD OIDC tokens — no manual upsert.
/// </summary>
public sealed record RepositoryDto
{
    public required Guid Id { get; init; }
    public required string Provider { get; init; }
    public required string Owner { get; init; }
    public required string Repo { get; init; }

    public static RepositoryDto From(RepoServerDb.SourceRepository r) => new()
    {
        Id = r.Id,
        Provider = r.Provider.ToString().ToLowerInvariant(),
        Owner = r.Owner,
        Repo = r.Repo
    };
}
