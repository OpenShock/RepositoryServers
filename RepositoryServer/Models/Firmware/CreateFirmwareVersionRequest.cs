namespace OpenShock.RepositoryServer.Models.Firmware;

public sealed class CreateFirmwareVersionRequest
{
    public required string Channel { get; init; }
    public required DateTimeOffset ReleaseDate { get; init; }
    public required string CommitHash { get; init; }
    public string? ReleaseUrl { get; init; }
    public Dictionary<string, Dictionary<string, FirmwareArtifactUpload>>? Artifacts { get; init; }
    public List<FirmwareReleaseNoteUpload> ReleaseNotes { get; init; } = [];
}
