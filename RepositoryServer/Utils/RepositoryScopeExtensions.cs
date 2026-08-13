using OpenShock.RepositoryServer.Enums;

namespace OpenShock.RepositoryServer.Utils;

public static class RepositoryScopeExtensions
{
    /// <summary>
    /// Wire form of a scope, used both as the claim value and in the admin API.
    /// </summary>
    public static string ToScopeClaim(this RepositoryScope scope) => scope switch
    {
        RepositoryScope.PublishFirmware => "publish_firmware",
        RepositoryScope.PublishModules => "publish_modules",
        _ => scope.ToString().ToLowerInvariant()
    };

    public static bool TryParseScope(string value, out RepositoryScope scope)
    {
        switch (value.Trim().ToLowerInvariant())
        {
            case "publish_firmware":
                scope = RepositoryScope.PublishFirmware;
                return true;
            case "publish_modules":
                scope = RepositoryScope.PublishModules;
                return true;
            default:
                scope = default;
                return false;
        }
    }
}
