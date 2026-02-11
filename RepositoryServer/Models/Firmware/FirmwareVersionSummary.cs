namespace OpenShock.RepositoryServer.Models.Firmware;

public sealed class FirmwareVersionSummary
{
    public required string Version { get; init; }
    public required string Channel { get; init; }
    public required DateTimeOffset ReleaseDate { get; init; }
}
