namespace OpenShock.RepositoryServer.AuthenticationHandlers;

/// <summary>
/// How admin authentication was wired up at startup, for the few places that have to behave
/// differently when there is no identity provider to talk to.
/// </summary>
/// <param name="DevBypass">
/// True when the Authentik login was replaced by the development bypass. Only ever true in a
/// Development-environment Debug build that explicitly asked for it.
/// </param>
public sealed record AdminAuthMode(bool DevBypass)
{
    /// <summary>
    /// Group the bypassed principal claims when no Authentik section supplies one. Lives here rather
    /// than on the handler so it is available to code that is compiled into Release builds too.
    /// </summary>
    public const string DevFallbackGroup = "dev-admin";
}
