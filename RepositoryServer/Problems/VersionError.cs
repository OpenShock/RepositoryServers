using System.Net;
using OpenShock.Internal.Common.Problems;

namespace OpenShock.RepositoryServer.Problems;

public static class VersionError
{
    public static OpenShockProblem VersionNotFound => new OpenShockProblem("Version.NotFound", "The version provided was not found");
    public static OpenShockProblem VersionAlreadyExists => new OpenShockProblem("Version.AlreadyExists", "This version has already been published and is immutable", HttpStatusCode.Conflict);
    public static OpenShockProblem VersionInvalidSemver => new OpenShockProblem("Version.InvalidSemVersion", "The version provided is not a valid Semantic Versioning string");
}