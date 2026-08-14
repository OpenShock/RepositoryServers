using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using OpenShock.RepositoryServer.Errors;
using OpenShock.RepositoryServer.JsonSerialization;

namespace OpenShock.RepositoryServer.AuthenticationHandlers;

/// <summary>
/// Answers a failed authorization with a problem body naming the requirements that were not met,
/// instead of the framework default of a bare 403.
/// </summary>
/// <remarks>
/// Only the versioned API surface is handled here. A browser hitting an admin page is left to the
/// default handler, which calls Forbid on the cookie scheme and lands in
/// <see cref="GitHubAuthentication.ConfigureCookie"/>'s access-denied event - the redirect a page
/// request wants. Challenges (401) always fall through for the same reason: they are what start a
/// login.
/// </remarks>
public sealed class OpenShockAuthorizationMiddlewareResultHandler : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler _defaultHandler = new();

    public Task HandleAsync(RequestDelegate next, HttpContext context, AuthorizationPolicy policy,
        PolicyAuthorizationResult authorizeResult)
    {
        if (authorizeResult.Forbidden && ApiSurface.IsApiPath(context.Request.Path))
        {
            var failedRequirements = authorizeResult.AuthorizationFailure?.FailedRequirements
                                         .Select(x => x.ToString() ?? "error") ?? [];

            return AuthorizationError.PolicyNotMet(failedRequirements)
                .WriteAsJsonAsync(context, JsonOptions.Default, context.RequestAborted);
        }

        return _defaultHandler.HandleAsync(next, context, policy, authorizeResult);
    }
}
