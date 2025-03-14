using OpenShock.Desktop.RepositoryServer.Utils;

namespace OpenShock.Desktop.RepositoryServer.Config;

public sealed class MetricsConfig
{
    public IReadOnlyCollection<string> AllowedNetworks { get; init; } = TrustedProxiesFetcher.PrivateNetworks;
}