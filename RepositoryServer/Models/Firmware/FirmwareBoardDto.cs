namespace OpenShock.RepositoryServer.Models.Firmware;

public sealed class FirmwareBoardDto
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string ChipId { get; init; }
    public required string ChipName { get; init; }
    public required bool Discontinued { get; init; }
}
