using System.Net;
using OpenShock.RepositoryServer.Problems;

namespace OpenShock.RepositoryServer.Errors;

public static class AuthResultError
{
    public static OpenShockProblem UnknownError => new("Authentication.UnknownError", "An unknown error occurred.", HttpStatusCode.InternalServerError);
    public static OpenShockProblem TokenInvalid => new("Authentication.TokenInvalid", "The token is invalid", HttpStatusCode.Unauthorized);

    /// <summary>No admin session, or one that has expired. The caller needs to log in at /auth/login.</summary>
    public static OpenShockProblem SessionRequired => new("Authentication.SessionRequired",
        "An admin session is required", HttpStatusCode.Unauthorized,
        "Sign in at /auth/login.");

    /// <summary>Authenticated with Authentik, but outside the configured admin group.</summary>
    public static OpenShockProblem NotAnAdmin => new("Authentication.NotAnAdmin",
        "Account is not an administrator", HttpStatusCode.Forbidden,
        "This account is not a member of the group required to administer this server.");

    /// <summary>The round trip to Authentik did not complete. Detail is deliberately vague.</summary>
    public static OpenShockProblem LoginFailed => new("Authentication.LoginFailed",
        "Login failed", HttpStatusCode.BadRequest,
        "The login could not be completed. Try again.");

    /// <summary>A returnUrl that would have sent the browser off this origin after login.</summary>
    public static OpenShockProblem InvalidReturnUrl => new("Authentication.InvalidReturnUrl",
        "Invalid return url", HttpStatusCode.BadRequest,
        "returnUrl must be a relative path on this server.");
}
