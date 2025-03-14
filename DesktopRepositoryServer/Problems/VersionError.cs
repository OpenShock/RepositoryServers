namespace OpenShock.Desktop.RepositoryServer.Problems;

public static class VersionError
{
    public static OpenShockProblem VersionNotFound => new OpenShockProblem("Version.NotFound", "The version provided was not found");
    public static OpenShockProblem VersionInvalidSemver => new OpenShockProblem("Version.InvalidSemVersion", "The version provided is not a valid Semantic Versioning string");
}