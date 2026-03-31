namespace OpenShock.RepositoryServer.Models.Firmware;

public sealed class CreateFirmwareChipRequest
{
    public required string Name { get; init; }
    public string? Architecture { get; init; }
}
