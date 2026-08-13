namespace OpenShock.RepositoryServer;

public static class AuthSchemas
{
    /// <summary>
    /// Session cookie issued after a successful GitHub login. This is what admin endpoints
    /// actually authorize against; <see cref="AdminOAuth"/> only establishes it.
    /// </summary>
    public const string AdminCookie = "AdminCookie";

    /// <summary>
    /// The OAuth challenge scheme pointing at GitHub. Only ever used to start a login, never to
    /// authorize a request.
    /// </summary>
    public const string AdminOAuth = "AdminOAuth";

    public const string CiCdToken = "CiCdToken";

    /// <summary>Authorization policies layered on top of the authentication schemes.</summary>
    public static class Policies
    {
        /// <summary>
        /// Guards every admin endpoint. Requires a GitHub session whose principal carries the
        /// configured admin team.
        /// </summary>
        public const string Admin = "Admin";

        public const string PublishFirmware = "PublishFirmware";
        public const string PublishModules = "PublishModules";
    }

    /// <summary>
    /// Claim keys attached to the admin principal after a successful GitHub login.
    /// </summary>
    public static class AdminClaims
    {
        /// <summary>
        /// Slug of the GitHub team the login was accepted for. Written by us at the callback rather
        /// than read off GitHub, so it is present exactly when membership was verified.
        /// </summary>
        public const string Team = "openshock:team";

        /// <summary>
        /// GitHub's numeric user id. Used for audit logging rather than authorization, and preferred
        /// over the login because ids are stable across renames.
        /// </summary>
        public const string Subject = "sub";

        /// <summary>GitHub login, for logs and the UI. Never authorize on this: logins are mutable.</summary>
        public const string Username = "preferred_username";
    }

    /// <summary>
    /// Claim keys attached to the CI/CD principal after successful GitHub OIDC validation.
    /// </summary>
    public static class CiCdClaims
    {
        public const string RepositoryId = "openshock:repository_id";
        public const string CommitHash = "openshock:commit_hash";
        public const string Ref = "openshock:ref";
        public const string RunId = "openshock:run_id";

        /// <summary>
        /// One claim per <see cref="OpenShock.RepositoryServer.Enums.RepositoryScope"/> granted to the
        /// calling repository. Authorization policies match on these.
        /// </summary>
        public const string Scope = "openshock:scope";
    }
}
