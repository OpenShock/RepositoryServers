namespace OpenShock.RepositoryServer;

public static class AuthSchemas
{
    /// <summary>
    /// Session cookie issued after a successful Authentik login. This is what admin endpoints
    /// actually authorize against; <see cref="AdminOidc"/> only establishes it.
    /// </summary>
    public const string AdminCookie = "AdminCookie";

    /// <summary>
    /// The OpenID Connect challenge scheme pointing at Authentik. Only ever used to start a login,
    /// never to authorize a request.
    /// </summary>
    public const string AdminOidc = "AdminOidc";

    public const string CiCdToken = "CiCdToken";

    /// <summary>Authorization policies layered on top of the authentication schemes.</summary>
    public static class Policies
    {
        /// <summary>
        /// Guards every admin endpoint. Requires an Authentik session whose principal carries the
        /// configured admin group.
        /// </summary>
        public const string Admin = "Admin";

        public const string PublishFirmware = "PublishFirmware";
        public const string PublishModules = "PublishModules";
    }

    /// <summary>
    /// Claim keys attached to the admin principal after a successful Authentik login.
    /// </summary>
    public static class AdminClaims
    {
        /// <summary>
        /// Group membership as reported by Authentik. The provider must be configured to emit
        /// <c>groups</c>; without it every login is rejected, since nothing can satisfy the policy.
        /// </summary>
        public const string Group = "groups";

        /// <summary>Stable subject identifier, used for audit logging rather than authorization.</summary>
        public const string Subject = "sub";

        /// <summary>Human-readable identity for logs. Never authorize on this: usernames are mutable.</summary>
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
