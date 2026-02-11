namespace OpenShock.RepositoryServer.Models.Firmware;

public sealed class CreateFirmwareBoardRequest
{
    public required string Name { get; init; }
    public required string ChipId { get; init; }
}
