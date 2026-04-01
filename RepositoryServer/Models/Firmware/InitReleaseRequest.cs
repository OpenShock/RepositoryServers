namespace OpenShock.RepositoryServer.Models.Firmware;

public sealed class InitReleaseRequest
{
    public required string Version { get; init; }
    public required string Channel { get; init; }
    public required string CommitHash { get; init; }
    public required DateTimeOffset ReleaseDate { get; init; }
    public string? ReleaseUrl { get; init; }
    public required List<string> Boards { get; init; }
    public List<FirmwareReleaseNoteUpload> ReleaseNotes { get; init; } = [];
}
