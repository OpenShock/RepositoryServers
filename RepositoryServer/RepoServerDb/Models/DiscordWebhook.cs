using OpenShock.RepositoryServer.Enums;

namespace OpenShock.RepositoryServer.RepoServerDb.Models;

/// <summary>
/// A Discord webhook to notify, and which events it wants.
/// </summary>
/// <remarks>
/// <see cref="Url"/> is a credential: possession of it is authorization to post to that channel. It is
/// therefore never returned by the API — the admin endpoints echo a masked form instead — and it is
/// the reason the notification service reads these rows rather than exposing them anywhere else.
/// </remarks>
public sealed class DiscordWebhook
{
    public Guid Id { get; set; }

    /// <summary>Operator-facing label, e.g. "releases channel". Not used for delivery.</summary>
    public required string Name { get; set; }

    public required string Url { get; set; }

    /// <summary>Events this webhook receives. Empty means it receives nothing.</summary>
    public DiscordNotificationEvent[] Events { get; set; } = [];

    public bool Enabled { get; set; } = true;
}
