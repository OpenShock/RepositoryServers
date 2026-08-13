using System;
using System.Collections.Generic;

namespace OpenShock.RepositoryServer.RepoServerDb;

public partial class Module
{
    public string Id { get; set; } = null!;

    public string Name { get; set; } = null!;

    public string Description { get; set; } = null!;

    public Uri? SourceUrl { get; set; } = null!;

    public Uri? IconUrl { get; set; }

    /// <summary>
    /// Repository authorized to publish versions of this module. Null means no repository may publish
    /// to it — modules are opt-in, so an unassigned module cannot be written to by any CI/CD principal.
    /// </summary>
    public Guid? RepositoryId { get; set; }

    public virtual SourceRepository? RepositoryNavigation { get; set; }

    public virtual ICollection<Version> Versions { get; set; } = new List<Version>();
}
