using System;
using System.Collections.Generic;

namespace OpenShock.Desktop.RepositoryServer.RepoServerDb;

public partial class Module
{
    public string Id { get; set; } = null!;

    public string Name { get; set; } = null!;

    public string Description { get; set; } = null!;

    public Uri? SourceUrl { get; set; } = null!;

    public Uri? IconUrl { get; set; }

    public virtual ICollection<Version> Versions { get; set; } = new List<Version>();
}
