using OpenShock.RepositoryServer.RepoServerDb;
using OpenShock.RepositoryServer.RepoServerDb.Models;
using OpenShock.RepositoryServer.Utils;

namespace OpenShock.RepositoryServer.Models.Discord;

/// <summary>
/// Admin view of a webhook. The URL is masked — it is the credential, so it is write-only.
/// </summary>
public sealed record DiscordWebhookDto
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }

    /// <summary>Masked URL, retaining the Discord webhook id so a row can be matched to a channel.</summary>
    public required string UrlMasked { get; init; }

    public required List<string> Events { get; init; }
    public required bool Enabled { get; init; }

    public static DiscordWebhookDto From(DiscordWebhook w) => new()
    {
        Id = w.Id,
        Name = w.Name,
        UrlMasked = DiscordWebhookMasking.Mask(w.Url),
        Events = w.Events.Select(e => e.ToEventName()).ToList(),
        Enabled = w.Enabled
    };
}
