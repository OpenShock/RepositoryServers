using System;
using System.Collections.Generic;

namespace OpenShock.Desktop.RepositoryServer.RepoServerDb;

public partial class Version
{
    public string VersionName { get; set; } = null!;

    public string Module { get; set; } = null!;

    public Uri ZipUrl { get; set; } = null!;

    public byte[] HashSha256 { get; set; } = null!;

    public Uri? ChangelogUrl { get; set; }

    public Uri? ReleaseUrl { get; set; }

    public virtual Module ModuleNavigation { get; set; } = null!;
}
