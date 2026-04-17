using System.ComponentModel.DataAnnotations;

namespace OpenShock.RepositoryServer.Config;

public sealed class FirmwareAdvisoryConfig
{
    [Required(AllowEmptyStrings = false)] public required string Severity { get; init; }
    [Required(AllowEmptyStrings = false)] public required string Title { get; init; }
    [Required(AllowEmptyStrings = false)] public required string Content { get; init; }
    [Required(AllowEmptyStrings = false)] public required string AffectedVersions { get; init; }
    public string? Url { get; init; }
}
