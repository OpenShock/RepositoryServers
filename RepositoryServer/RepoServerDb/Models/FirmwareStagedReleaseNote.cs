using OpenShock.RepositoryServer.Enums;

namespace OpenShock.RepositoryServer.RepoServerDb.Models;

public sealed class FirmwareStagedReleaseNote
{
    public required Guid ReleaseId { get; set; }
    public required int Index { get; set; }
    public required ReleaseNoteSectionType SectionType { get; set; }
    public string? Title { get; set; }
    public required string Content { get; set; }

    public FirmwareRelease ReleaseNavigation { get; set; } = null!;
}
