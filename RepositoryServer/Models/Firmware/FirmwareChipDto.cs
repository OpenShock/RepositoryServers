namespace OpenShock.RepositoryServer.Models.Firmware;

public sealed class FirmwareChipDto
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string? Architecture { get; init; }
}
