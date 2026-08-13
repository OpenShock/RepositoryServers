using System.ComponentModel.DataAnnotations;

namespace OpenShock.RepositoryServer.Models.Firmware;

public sealed class CreateFirmwareBoardRequest
{
    /// <summary>
    /// Unique board identifier visible to clients, and the board's path segment in artifact URLs
    /// and storage keys. Hubs send this as their board reference — it is their PlatformIO env name,
    /// e.g. <c>"Wemos-D1-Mini-ESP32"</c>.
    /// </summary>
    /// <remarks>
    /// Restricted to URL-safe characters because it is interpolated into CDN paths unescaped.
    /// Notably this rejects <c>/</c>, which would otherwise let a board name reshape the storage key.
    /// </remarks>
    [Required(AllowEmptyStrings = false)]
    [MaxLength(128)]
    [RegularExpression(@"^[A-Za-z0-9][A-Za-z0-9._-]*$",
        ErrorMessage = "Board name must start alphanumeric and contain only letters, digits, '.', '_' or '-'.")]
    public required string Name { get; init; }

    [Required]
    public required Guid ChipId { get; init; }

    public List<string>? RequiredArtifactTypes { get; init; }
}
