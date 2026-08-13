using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OpenShock.RepositoryServer.AuthenticationHandlers;
using OpenShock.RepositoryServer.Errors;

namespace OpenShock.RepositoryServer.Controllers;

/// <summary>
/// Login, logout, and session introspection for the admin surface.
/// </summary>
/// <remarks>
/// Deliberately unversioned and outside the <c>/1</c> and <c>/2</c> trees: these are browser
/// endpoints for humans, not part of the API contract that hubs and CI consume.
/// </remarks>
[ApiController]
[Route("/auth")]
[ApiExplorerSettings(IgnoreApi = true)]
public sealed class AuthController : OpenShockControllerBase
{
    private readonly AdminAuthMode _authMode;

    public AuthController(AdminAuthMode authMode)
    {
        _authMode = authMode;
    }

    /// <summary>
    /// Starts the Authentik login. Returns a redirect to the identity provider.
    /// </summary>
    [HttpGet("login")]
    [AllowAnonymous]
    public IActionResult Login([FromQuery] string? returnUrl)
    {
        // An open redirect here would be worth something: the victim arrives from a real login on the
        // real origin, which is exactly the context in which people stop reading the address bar.
        if (!string.IsNullOrEmpty(returnUrl) && !Url.IsLocalUrl(returnUrl))
        {
            return Problem(AuthResultError.InvalidReturnUrl);
        }

        // Under the development bypass there is no identity provider and no OIDC scheme registered,
        // so there is nothing to challenge. The caller is already an admin.
        if (_authMode.DevBypass)
        {
            return LocalRedirect(returnUrl ?? "/");
        }

        return Challenge(
            new AuthenticationProperties { RedirectUri = returnUrl ?? "/" },
            AuthSchemas.AdminOidc);
    }

    /// <summary>
    /// Ends the local session and the Authentik session it came from.
    /// </summary>
    [HttpGet("logout")]
    [AllowAnonymous]
    public IActionResult Logout()
    {
        if (_authMode.DevBypass)
        {
            return LocalRedirect("/");
        }

        // Dropping only the cookie would leave the Authentik session intact, so the next visit to
        // /auth/login would silently sign the same account straight back in.
        return SignOut(
            new AuthenticationProperties { RedirectUri = "/" },
            AuthSchemas.AdminCookie,
            AuthSchemas.AdminOidc);
    }

    /// <summary>
    /// Reports the current admin session. Used by the admin UI to decide whether to show a login.
    /// </summary>
    [HttpGet("me")]
    [Authorize(Policy = AuthSchemas.Policies.Admin)]
    public IActionResult Me() => Ok(new
    {
        subject = User.FindFirstValue(AuthSchemas.AdminClaims.Subject),
        username = User.FindFirstValue(AuthSchemas.AdminClaims.Username),
        groups = User.FindAll(AuthSchemas.AdminClaims.Group).Select(c => c.Value).ToArray()
    });
}
