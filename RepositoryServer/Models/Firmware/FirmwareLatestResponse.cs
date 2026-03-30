namespace OpenShock.RepositoryServer.Models.Firmware;

public sealed class FirmwareLatestResponse
{
    public required string Version { get; init; }
    public required string Channel { get; init; }
    public required DateTimeOffset ReleaseDate { get; init; }
    public required Dictionary<string, List<FirmwareArtifactDto>> Artifacts { get; init; }
}
