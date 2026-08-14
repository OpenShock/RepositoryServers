namespace OpenShock.RepositoryServer;

/// <summary>
/// Tells the versioned API surface apart from the browser-facing admin UI.
/// </summary>
/// <remarks>
/// The two want different answers to the same authentication or authorization failure: an API caller
/// wants a status code and a problem body, a browser wants to be sent to the login page. Deciding on
/// path rather than sniffing Accept headers keeps the behaviour identical for curl, fetch and the
/// admin UI.
/// </remarks>
public static class ApiSurface
{
    /// <summary>
    /// True for anything under the versioned API prefixes; everything else is assumed to be a browser
    /// that can usefully follow a redirect.
    /// </summary>
    public static bool IsApiPath(PathString path) =>
        path.StartsWithSegments("/1") || path.StartsWithSegments("/2");
}
