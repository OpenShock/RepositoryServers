namespace OpenShock.RepositoryServer.Models.Firmware;

public sealed class InitReleaseResponse
{
    public required Guid Id { get; init; }
    public required string Status { get; init; }
}
