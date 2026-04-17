using System.ComponentModel.DataAnnotations;

namespace OpenShock.RepositoryServer.Models.Firmware;

public sealed class CreateFirmwareBoardRequest
{
    [Required(AllowEmptyStrings = false)]
    [MaxLength(128)]
    public required string Name { get; init; }

    [Required]
    public required Guid ChipId { get; init; }

    public List<string>? RequiredArtifactTypes { get; init; }
}
