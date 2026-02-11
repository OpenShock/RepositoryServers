namespace OpenShock.RepositoryServer.Models.Firmware;

public sealed class FirmwareReleaseNoteUpload
{
    public required string Type { get; init; }
    public string? Title { get; init; }
    public required string Content { get; init; }
}
