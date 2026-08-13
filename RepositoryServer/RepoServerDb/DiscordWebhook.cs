using OpenShock.RepositoryServer.Enums;

namespace OpenShock.RepositoryServer.RepoServerDb;

/// <summary>
/// A Discord webhook to notify, and which events it wants.
/// </summary>
/// <remarks>
/// <see cref="Url"/> is a credential: possession of it is authorization to post to that channel. It is
/// therefore never returned by the API — the admin endpoints echo a masked form instead — and it is
/// the reason the notification service reads these rows rather than exposing them anywhere else.
/// </remarks>
public class DiscordWebhook
{
    public Guid Id { get; set; }

    /// <summary>Operator-facing label, e.g. "releases channel". Not used for delivery.</summary>
    public string Name { get; set; } = null!;

    public string Url { get; set; } = null!;

    /// <summary>Events this webhook receives. Empty means it receives nothing.</summary>
    public DiscordNotificationEvent[] Events { get; set; } = [];

    public bool Enabled { get; set; } = true;
}
