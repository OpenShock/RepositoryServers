namespace OpenShock.RepositoryServer.RepoServerDb.Models;

public sealed class Module
{
    public required string Id { get; set; }

    public required string Name { get; set; }

    public required string Description { get; set; }

    public Uri? SourceUrl { get; set; }

    public Uri? IconUrl { get; set; }

    /// <summary>
    /// Repository authorized to publish versions of this module. Null means no repository may publish
    /// to it — modules are opt-in, so an unassigned module cannot be written to by any CI/CD principal.
    /// </summary>
    public Guid? RepositoryId { get; set; }

    public SourceRepository? RepositoryNavigation { get; set; }

    public ICollection<Version> Versions { get; } = [];
}
