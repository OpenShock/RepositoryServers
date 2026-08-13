using OpenShock.RepositoryServer.Enums;

namespace OpenShock.RepositoryServer.RepoServerDb.Models;

public sealed class FirmwareReleaseNote
{
    public required string Version { get; set; }
    public required int Index { get; set; }
    public required ReleaseNoteSectionType SectionType { get; set; }
    public string? Title { get; set; }
    public required string Content { get; set; }

    public FirmwareVersion VersionNavigation { get; set; } = null!;
}
