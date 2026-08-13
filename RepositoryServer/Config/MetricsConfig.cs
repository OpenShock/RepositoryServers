using OpenShock.RepositoryServer.Utils;

namespace OpenShock.RepositoryServer.Config;

public sealed class MetricsConfig
{
    public IReadOnlyCollection<string> AllowedNetworks { get; init; } = TrustedProxiesFetcher.PrivateNetworks;
}