using System.Net;
using OpenShock.Desktop.RepositoryServer.Problems;

namespace OpenShock.Desktop.RepositoryServer.Errors;

public static class AuthResultError
{
    public static OpenShockProblem UnknownError => new("Authentication.UnknownError", "An unknown error occurred.", HttpStatusCode.InternalServerError);
    public static OpenShockProblem TokenInvalid => new("Authentication.TokenInvalid", "The token is invalid", HttpStatusCode.Unauthorized);
}