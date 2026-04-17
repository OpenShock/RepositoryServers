namespace OpenShock.RepositoryServer;

public static class AuthSchemas
{
    public const string AdminToken = "AdminToken";
    public const string CiCdToken = "CiCdToken";

    /// <summary>
    /// Claim keys attached to the CI/CD principal after successful GitHub OIDC validation.
    /// </summary>
    public static class CiCdClaims
    {
        public const string RepositoryId = "openshock:repository_id";
        public const string CommitHash = "openshock:commit_hash";
        public const string Ref = "openshock:ref";
        public const string RunId = "openshock:run_id";
    }
}
