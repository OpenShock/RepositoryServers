using System.Net;
using OpenShock.Internal.Common.Problems;

namespace OpenShock.RepositoryServer.Errors;

public static class VersionError
{
    public static OpenShockProblem VersionNotFound => new("Version.NotFound", "The version provided was not found", HttpStatusCode.NotFound);
    public static OpenShockProblem VersionAlreadyExists => new("Version.AlreadyExists", "This version has already been published and is immutable", HttpStatusCode.Conflict);
    public static OpenShockProblem VersionInvalidSemver => new("Version.InvalidSemVersion", "The version provided is not a valid Semantic Versioning string");
}
