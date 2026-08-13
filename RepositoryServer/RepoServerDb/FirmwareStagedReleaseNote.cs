using OpenShock.RepositoryServer.Enums;

namespace OpenShock.RepositoryServer.RepoServerDb;

public class FirmwareStagedReleaseNote
{
    public Guid ReleaseId { get; set; }
    public int Index { get; set; }
    public ReleaseNoteSectionType SectionType { get; set; }
    public string? Title { get; set; }
    public string Content { get; set; } = null!;

    public virtual FirmwareRelease ReleaseNavigation { get; set; } = null!;
}
