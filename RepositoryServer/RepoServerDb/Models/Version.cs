namespace OpenShock.RepositoryServer.RepoServerDb.Models;

public sealed class Version
{
    public required string VersionName { get; set; }

    public required string Module { get; set; }

    public required Uri ZipUrl { get; set; }

    public required byte[] HashSha256 { get; set; }

    public Uri? ChangelogUrl { get; set; }

    public Uri? ReleaseUrl { get; set; }

    public Module ModuleNavigation { get; set; } = null!;
}
