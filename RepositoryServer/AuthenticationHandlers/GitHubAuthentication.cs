using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Json;
using AspNet.Security.OAuth.GitHub;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OAuth;
using OpenShock.Internal.Common.Problems;
using OpenShock.RepositoryServer.Config;
using OpenShock.RepositoryServer.Errors;
using OpenShock.RepositoryServer.JsonSerialization;

namespace OpenShock.RepositoryServer.AuthenticationHandlers;

/// <summary>
/// Configures admin authentication as a GitHub OAuth authorization code flow, with the resulting
/// session held in a cookie.
/// </summary>
/// <remarks>
/// Two schemes cooperate. <see cref="AuthSchemas.AdminOAuth"/> runs the login and then hands off;
/// the thing every admin request is actually authorized against is the
/// <see cref="AuthSchemas.AdminCookie"/> session. GitHub is therefore consulted once per login, not
/// once per request.
///
/// Team membership is checked at login and baked into the cookie, which means a user removed from
/// the team keeps access until their session expires. That is the standard tradeoff for cookie
/// sessions; <see cref="GitHubAuthConfig.SessionLifetime"/> bounds it.
/// </remarks>
public static class GitHubAuthentication
{
    public static void ConfigureCookie(CookieAuthenticationOptions options, GitHubAuthConfig config)
    {
        options.Cookie.Name = ".OpenShock.RepoServer.Admin";
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;

        // Lax rather than Strict: the OAuth callback is a cross-site top-level navigation back from
        // GitHub, and Strict would drop the cookie on that hop, leaving the user in a login loop.
        options.Cookie.SameSite = SameSiteMode.Lax;

        options.ExpireTimeSpan = config.SessionLifetime;
        options.SlidingExpiration = false;

        // Left at their defaults these point at /Account/Login and /Account/AccessDenied, which is
        // ASP.NET Identity's scaffolding and does not exist here: an anonymous visit to /admin ends
        // on the 404 page instead of the login. The parameter name matches AuthController.Login's
        // argument so the round trip back to the requested page needs no rebinding.
        options.LoginPath = "/auth/login";
        options.ReturnUrlParameter = "returnUrl";

        // API callers get a status code, browsers get sent to the login. Deciding on path rather than
        // sniffing Accept headers keeps the behaviour identical for curl, fetch and the admin UI.
        options.Events = new CookieAuthenticationEvents
        {
            OnRedirectToLogin = context => WriteProblemOrRedirect(context, AuthResultError.SessionRequired),

            // No redirect target for this one: a session only exists once team membership was
            // verified at the callback, so a denied session is a state change, not a wrong turn a
            // login page could fix. Answering with the problem beats bouncing to a page that would
            // challenge and sign the same rejected user straight back in.
            OnRedirectToAccessDenied = context => WriteProblemAsync(context.HttpContext, AuthResultError.NotAnAdmin)
        };
    }

    public static void ConfigureOAuth(GitHubAuthenticationOptions options, GitHubAuthConfig config)
    {
        options.ClientId = config.ClientId;
        options.ClientSecret = config.ClientSecret;

        options.SignInScheme = AuthSchemas.AdminCookie;
        options.CallbackPath = config.CallbackPath;

        // The token is used once, during the callback, to ask GitHub about team membership, and is
        // then discarded. Persisting it would put a credential that can read the org into a cookie
        // for no benefit: authorization is decided by the claims captured below.
        options.SaveTokens = false;

        // read:org is what makes the team membership endpoint answer. Without it GitHub returns 404
        // for a team the user is genuinely in, which is indistinguishable from not being a member.
        options.Scope.Clear();
        options.Scope.Add("read:org");

        options.Events = new OAuthEvents
        {
            OnCreatingTicket = context => OnCreatingTicketAsync(context, config),
            OnRemoteFailure = OnRemoteFailureAsync
        };
    }

    /// <summary>
    /// Rejects non-members at the callback and trims the principal down to what the session needs.
    /// </summary>
    /// <remarks>
    /// The handler has already populated the principal from <c>/user</c>. What it cannot know is
    /// whether this account is one of ours, so that question is asked here, before any session
    /// exists. Refusing at the callback rather than issuing a session means an outsider gets a clear
    /// 403 instead of a login that appears to work and then 403s on every subsequent call.
    /// </remarks>
    private static async Task OnCreatingTicketAsync(OAuthCreatingTicketContext context, GitHubAuthConfig config)
    {
        var identity = context.Identity;
        if (identity is null)
        {
            await WriteFailureAsync(context, AuthResultError.LoginFailed);
            return;
        }

        var login = identity.FindFirst(ClaimTypes.Name)?.Value
                    ?? identity.FindFirst(GitHubAuthenticationConstants.Claims.Name)?.Value;
        var userId = identity.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        if (string.IsNullOrWhiteSpace(login))
        {
            await WriteFailureAsync(context, AuthResultError.LoginFailed);
            return;
        }

        var isMember = await IsActiveTeamMemberAsync(context, config, login);
        if (!isMember)
        {
            await WriteFailureAsync(context, AuthResultError.NotAnAdmin);
            return;
        }

        // Rebuild rather than append: the handler's principal carries the GitHub profile (avatar,
        // company, bio), none of which this server has a reason to hold in a cookie.
        var claims = new List<Claim>
        {
            new(AuthSchemas.AdminClaims.Username, login),
            new(AuthSchemas.AdminClaims.Team, config.Team)
        };

        if (!string.IsNullOrWhiteSpace(userId))
        {
            // The numeric id, not the login: GitHub logins can be changed and reused, ids cannot.
            claims.Add(new Claim(AuthSchemas.AdminClaims.Subject, userId));
        }

        var trimmed = new ClaimsIdentity(claims, AuthSchemas.AdminCookie,
            AuthSchemas.AdminClaims.Username, AuthSchemas.AdminClaims.Team);

        context.Principal = new ClaimsPrincipal(trimmed);
    }

    /// <summary>
    /// Asks GitHub whether <paramref name="login"/> is an active member of the configured team.
    /// </summary>
    /// <remarks>
    /// <c>state</c> matters: an invited-but-not-accepted user comes back 200 with
    /// <c>"pending"</c>, and treating that as membership would let anyone who has merely been
    /// invited administer the server.
    /// </remarks>
    private static async Task<bool> IsActiveTeamMemberAsync(
        OAuthCreatingTicketContext context, GitHubAuthConfig config, string login)
    {
        var url = $"https://api.github.com/orgs/{Uri.EscapeDataString(config.Organization)}" +
                  $"/teams/{Uri.EscapeDataString(config.Team)}" +
                  $"/memberships/{Uri.EscapeDataString(login)}";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", context.AccessToken);
        request.Headers.UserAgent.ParseAdd("OpenShock.RepositoryServer");

        using var response = await context.Backchannel.SendAsync(request, context.HttpContext.RequestAborted);

        // 404 is the answer for both "not a member" and "no permission to see the team", which are
        // the same outcome here.
        if (response.StatusCode == HttpStatusCode.NotFound) return false;

        if (!response.IsSuccessStatusCode)
        {
            Logger(context).LogWarning(
                "GitHub team membership check for {Login} failed with {Status}", login, response.StatusCode);
            return false;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(context.HttpContext.RequestAborted);
        using var payload = await JsonDocument.ParseAsync(stream, cancellationToken: context.HttpContext.RequestAborted);

        return payload.RootElement.TryGetProperty("state", out var state) &&
               state.ValueKind == JsonValueKind.String &&
               string.Equals(state.GetString(), "active", StringComparison.Ordinal);
    }

    /// <summary>
    /// Turns a failed round trip to GitHub into a problem response instead of an unhandled
    /// exception, which is what the default does.
    /// </summary>
    private static Task OnRemoteFailureAsync(RemoteFailureContext context)
    {
        context.HttpContext.RequestServices
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger(typeof(GitHubAuthentication))
            .LogWarning(context.Failure, "GitHub login failed");

        context.HandleResponse();
        return WriteProblemAsync(context.HttpContext, AuthResultError.LoginFailed);
    }

    private static ILogger Logger(OAuthCreatingTicketContext context) =>
        context.HttpContext.RequestServices
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger(typeof(GitHubAuthentication));

    private static Task WriteFailureAsync(OAuthCreatingTicketContext context, OpenShockProblem problem)
    {
        // Suppresses the ticket so no session is created, and answers the callback ourselves.
        context.Fail(problem.Title ?? "Login rejected");
        return WriteProblemAsync(context.HttpContext, problem);
    }

    private static Task WriteProblemOrRedirect<TOptions>(
        RedirectContext<TOptions> context, OpenShockProblem problem)
        where TOptions : AuthenticationSchemeOptions
    {
        if (ApiSurface.IsApiPath(context.Request.Path))
        {
            return WriteProblemAsync(context.HttpContext, problem);
        }

        context.Response.Redirect(context.RedirectUri);
        return Task.CompletedTask;
    }

    private static Task WriteProblemAsync(HttpContext httpContext, OpenShockProblem problem)
    {
        if (httpContext.Response.HasStarted) return Task.CompletedTask;

        return problem.WriteAsJsonAsync(httpContext, JsonOptions.Default, httpContext.RequestAborted);
    }
}
