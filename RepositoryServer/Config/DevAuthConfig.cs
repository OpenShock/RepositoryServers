namespace OpenShock.RepositoryServer.Config;

/// <summary>
/// Local development escape hatch that skips the Authentik login and treats every request as an
/// administrator.
/// </summary>
/// <remarks>
/// Three independent gates have to line up before this does anything: the code is compiled out of
/// Release builds entirely, the environment must be Development, and this flag must be set. The
/// published container is built with <c>-c Release</c>, so the bypass does not exist in the image at
/// all, which is a stronger guarantee than any runtime check on an environment variable somebody
/// could set by accident.
/// </remarks>
public sealed class DevAuthConfig
{
    /// <summary>
    /// Grants admin to every caller. Ignored outside a Development-environment Debug build.
    /// </summary>
    public bool BypassAuthentik { get; init; }

    /// <summary>Name the bypassed session reports, so logs and the UI show something meaningful.</summary>
    public string Username { get; init; } = "dev-admin";
}
