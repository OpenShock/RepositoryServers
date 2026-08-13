using System.ComponentModel.DataAnnotations;

namespace OpenShock.RepositoryServer.Models.Firmware;

public sealed class FixChangelogRequest
{
    [Required(AllowEmptyStrings = true)]
    public required string Changelog { get; init; }
}
