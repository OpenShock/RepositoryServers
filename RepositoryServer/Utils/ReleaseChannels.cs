using OpenShock.RepositoryServer.Enums;
using OpenShock.RepositoryServer.RepoServerDb;

namespace OpenShock.RepositoryServer.Utils;

/// <summary>
/// Channel visibility and the deterministic ordering used to pick "latest".
/// </summary>
public static class ReleaseChannels
{
    /// <summary>
    /// Channels whose releases are visible to a subscriber of <paramref name="channel"/>.
    /// </summary>
    /// <remarks>
    /// Channels cascade: a stable release is also the newest thing a beta or develop subscriber should
    /// receive. CI already works this way — publishing a stable build advances the stable, beta and
    /// develop pointers together. Without the cascade, strict per-channel equality would pin a beta hub
    /// back to the last explicit release candidate and offer it a downgrade.
    /// </remarks>
    public static ReleaseChannel[] VisibleTo(ReleaseChannel channel) => channel switch
    {
        ReleaseChannel.Stable => [ReleaseChannel.Stable],
        ReleaseChannel.Beta => [ReleaseChannel.Stable, ReleaseChannel.Beta],
        ReleaseChannel.Develop => [ReleaseChannel.Stable, ReleaseChannel.Beta, ReleaseChannel.Develop],
        _ => [channel]
    };

    /// <summary>
    /// Orders versions newest-first with a total order.
    /// </summary>
    /// <remarks>
    /// <c>release_date</c> is supplied by the client and is not unique — the spec's own example uses
    /// midnight — so ordering by it alone leaves ties broken arbitrarily by the database. That let
    /// <c>/manifest</c>, <c>/latest/{channel}</c> and <c>/latest/{channel}/{board}</c> disagree with
    /// each other and flip between requests, which a hub sees as firmware flapping. It also made
    /// keyset-free pagination unstable, duplicating and dropping rows across pages. Version is the
    /// primary key, so adding it as a tiebreaker yields a total order.
    /// </remarks>
    public static IQueryable<FirmwareVersion> OrderByNewest(this IQueryable<FirmwareVersion> query) =>
        query.OrderByDescending(v => v.ReleaseDate).ThenByDescending(v => v.Version);
}
