namespace OpenShock.RepositoryServer.RepoServerDb;

public class FirmwareReleaseNote
{
    public string Version { get; set; } = null!;
    public int Index { get; set; }
    public FirmwareReleaseNoteType Type { get; set; }
    public string? Title { get; set; }
    public string Content { get; set; } = null!;

    public virtual FirmwareVersion VersionNavigation { get; set; } = null!;
}
