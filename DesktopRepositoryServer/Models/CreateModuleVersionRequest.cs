namespace OpenShock.Desktop.RepositoryServer.Models;

public sealed class CreateModuleVersionRequest
{
    public required Uri ZipUrl { get; init; }
    public required byte[] Sha256Hash { get; init; }
    public Uri? ChangelogUrl { get; init; } = null;
    public Uri? ReleaseUrl { get; init; } = null;
}