namespace OpenShock.RepositoryServer;

public static class AuthSchemas
{
    public const string AdminToken = "AdminToken";
    public const string CiCdToken = "CiCdToken";

    /// <summary>Authorization policies layered on top of the CI/CD scheme.</summary>
    public static class Policies
    {
        public const string PublishFirmware = "PublishFirmware";
        public const string PublishModules = "PublishModules";
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
