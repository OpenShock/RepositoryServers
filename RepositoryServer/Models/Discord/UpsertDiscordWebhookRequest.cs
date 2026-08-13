using System.ComponentModel.DataAnnotations;

namespace OpenShock.RepositoryServer.Models.Discord;

public sealed class UpsertDiscordWebhookRequest
{
    /// <summary>Operator-facing label, e.g. "releases channel".</summary>
    [Required(AllowEmptyStrings = false)]
    [MaxLength(128)]
    public required string Name { get; init; }

    /// <summary>
    /// Full Discord webhook URL. Write-only: it is never returned by any endpoint.
    /// </summary>
    [Required(AllowEmptyStrings = false)]
    [MaxLength(512)]
    [Url]
    public required string Url { get; init; }

    /// <summary>
    /// Events this webhook should receive: <c>firmware_release_published</c>,
    /// <c>desktop_module_version_published</c>, <c>release_notes_need_editing</c>,
    /// <c>staged_release_expired</c>. An empty list means it receives nothing.
    /// </summary>
    public List<string>? Events { get; init; }

    public bool Enabled { get; init; } = true;
}
