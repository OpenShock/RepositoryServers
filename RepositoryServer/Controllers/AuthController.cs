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
    /// Starts the GitHub login. Returns a redirect to GitHub's authorization page.
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

        // Under the development bypass there is no identity provider and no OAuth scheme registered,
        // so there is nothing to challenge. The caller is already an admin.
        if (_authMode.DevBypass)
        {
            return LocalRedirect(returnUrl ?? "/");
        }

        return Challenge(
            new AuthenticationProperties { RedirectUri = returnUrl ?? "/" },
            AuthSchemas.AdminOAuth);
    }

    /// <summary>
    /// Ends the local session.
    /// </summary>
    /// <remarks>
    /// Only the cookie is dropped. GitHub has no front-channel logout for OAuth apps, so the user
    /// stays signed in to GitHub itself and a later /auth/login will sign them straight back in
    /// without a prompt. Revoking that is done from the account's authorized-apps settings.
    /// </remarks>
    [HttpGet("logout")]
    [AllowAnonymous]
    public IActionResult Logout()
    {
        if (_authMode.DevBypass)
        {
            return LocalRedirect("/");
        }

        return SignOut(
            new AuthenticationProperties { RedirectUri = "/" },
            AuthSchemas.AdminCookie);
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
        team = User.FindFirstValue(AuthSchemas.AdminClaims.Team)
    });
}
