using System.Net.Mime;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OAuth.Claims;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using OpenShock.Internal.Common.Problems;
using OpenShock.RepositoryServer.Config;
using OpenShock.RepositoryServer.Errors;
using OpenShock.RepositoryServer.Problems;
using JsonOptions = Microsoft.AspNetCore.Http.Json.JsonOptions;

namespace OpenShock.RepositoryServer.AuthenticationHandlers;

/// <summary>
/// Configures admin authentication as an OpenID Connect authorization code flow against Authentik,
/// with the resulting session held in a cookie.
/// </summary>
/// <remarks>
/// Two schemes cooperate. <see cref="AuthSchemas.AdminOidc"/> runs the login and then hands off; the
/// thing every admin request is actually authorized against is the <see cref="AuthSchemas.AdminCookie"/>
/// session. Authentik is therefore consulted once per login, not once per request.
///
/// Group membership is read at login and baked into the cookie, which means a user removed from the
/// admin group in Authentik keeps access until their session expires. That is the standard tradeoff
/// for cookie sessions; <see cref="AuthentikConfig.SessionLifetime"/> bounds it.
/// </remarks>
public static class AuthentikAuthentication
{
    public static void ConfigureCookie(CookieAuthenticationOptions options, AuthentikConfig config)
    {
        options.Cookie.Name = ".OpenShock.RepoServer.Admin";
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;

        // Lax rather than Strict: the OIDC callback is a cross-site top-level navigation back from
        // Authentik, and Strict would drop the cookie on that hop, leaving the user in a login loop.
        options.Cookie.SameSite = SameSiteMode.Lax;

        options.ExpireTimeSpan = config.SessionLifetime;
        options.SlidingExpiration = false;

        // API callers get a status code, browsers get sent to the login. Deciding on path rather than
        // sniffing Accept headers keeps the behaviour identical for curl, fetch and the admin UI.
        options.Events = new CookieAuthenticationEvents
        {
            OnRedirectToLogin = context => WriteProblemOrRedirect(context, AuthResultError.SessionRequired),
            OnRedirectToAccessDenied = context => WriteProblemOrRedirect(context, AuthResultError.NotAnAdmin)
        };
    }

    public static void ConfigureOidc(OpenIdConnectOptions options, AuthentikConfig config)
    {
        options.Authority = config.Authority;
        options.ClientId = config.ClientId;
        options.ClientSecret = config.ClientSecret;
        options.RequireHttpsMetadata = config.RequireHttpsMetadata;

        options.SignInScheme = AuthSchemas.AdminCookie;
        options.CallbackPath = config.CallbackPath;
        options.SignedOutCallbackPath = config.SignedOutCallbackPath;

        // Code flow with PKCE. The secret is exchanged server-side, so no token ever reaches the
        // browser and there is nothing in the front channel worth stealing.
        options.ResponseType = OpenIdConnectResponseType.Code;
        options.UsePkce = true;
        options.ResponseMode = OpenIdConnectResponseMode.Query;

        // The access token buys nothing here: authorization is decided entirely by the group claim
        // captured below, so keeping tokens around would only enlarge the cookie.
        options.SaveTokens = false;

        options.Scope.Clear();
        options.Scope.Add("openid");
        options.Scope.Add("profile");
        options.Scope.Add("email");

        options.GetClaimsFromUserInfoEndpoint = true;
        options.MapInboundClaims = false;

        options.TokenValidationParameters = new TokenValidationParameters
        {
            NameClaimType = AuthSchemas.AdminClaims.Username,
            RoleClaimType = AuthSchemas.AdminClaims.Group,
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidAudience = config.ClientId,
            ValidateLifetime = true
        };

        // Authentik returns groups as a JSON array. The default claim actions map only scalars, so
        // without this a user in several groups contributes one claim, and which one is arbitrary.
        options.ClaimActions.Remove(AuthSchemas.AdminClaims.Group);
        options.ClaimActions.Add(new JsonArrayClaimAction(AuthSchemas.AdminClaims.Group));

        options.Events = new OpenIdConnectEvents
        {
            OnTicketReceived = context => OnTicketReceivedAsync(context, config),
            OnRemoteFailure = OnRemoteFailureAsync
        };
    }

    /// <summary>
    /// Rejects non-admins at the callback and trims the principal down to what the session needs.
    /// </summary>
    /// <remarks>
    /// Trimming matters for two reasons. The cookie carries every claim it is given, and Authentik
    /// hands out one per group, so a user in many groups can push the cookie past what proxies accept.
    /// It also keeps unrelated group membership, which is information about the user's access to other
    /// systems, out of a cookie that this server has no reason to hold.
    /// </remarks>
    private static Task OnTicketReceivedAsync(TicketReceivedContext context, AuthentikConfig config)
    {
        var principal = context.Principal;
        if (principal is null)
        {
            return WriteFailureAsync(context, AuthResultError.LoginFailed);
        }

        var isAdmin = principal.Claims.Any(c =>
            c.Type == AuthSchemas.AdminClaims.Group &&
            string.Equals(c.Value, config.AdminGroup, StringComparison.Ordinal));

        if (!isAdmin)
        {
            // Refusing here rather than issuing a session means a non-admin gets a clear 403 instead
            // of a login that appears to work and then 403s on every subsequent call.
            return WriteFailureAsync(context, AuthResultError.NotAnAdmin);
        }

        var claims = new List<Claim>
        {
            new(AuthSchemas.AdminClaims.Group, config.AdminGroup)
        };

        foreach (var claimType in new[] { AuthSchemas.AdminClaims.Subject, AuthSchemas.AdminClaims.Username })
        {
            if (principal.FindFirst(claimType) is { } claim)
            {
                claims.Add(new Claim(claimType, claim.Value));
            }
        }

        var identity = new ClaimsIdentity(claims, AuthSchemas.AdminCookie,
            AuthSchemas.AdminClaims.Username, AuthSchemas.AdminClaims.Group);
        context.Principal = new ClaimsPrincipal(identity);

        return Task.CompletedTask;
    }

    /// <summary>
    /// Turns a failed round trip to Authentik into a problem response instead of an unhandled
    /// exception, which is what the default does.
    /// </summary>
    private static Task OnRemoteFailureAsync(RemoteFailureContext context)
    {
        var logger = context.HttpContext.RequestServices
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger(typeof(AuthentikAuthentication));

        logger.LogWarning(context.Failure, "Authentik login failed");

        context.HandleResponse();
        return WriteProblemAsync(context.HttpContext, AuthResultError.LoginFailed);
    }

    private static Task WriteFailureAsync(TicketReceivedContext context, OpenShockProblem problem)
    {
        context.HandleResponse();
        return WriteProblemAsync(context.HttpContext, problem);
    }

    private static Task WriteProblemOrRedirect<TOptions>(
        RedirectContext<TOptions> context, OpenShockProblem problem)
        where TOptions : AuthenticationSchemeOptions
    {
        if (IsApiPath(context.Request.Path))
        {
            return WriteProblemAsync(context.HttpContext, problem);
        }

        context.Response.Redirect(context.RedirectUri);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Versioned API surface. Everything under these prefixes answers with a status code; anything
    /// else is assumed to be a browser that can usefully follow a redirect.
    /// </summary>
    private static bool IsApiPath(PathString path) =>
        path.StartsWithSegments("/1") || path.StartsWithSegments("/2");

    private static Task WriteProblemAsync(HttpContext httpContext, OpenShockProblem problem)
    {
        if (httpContext.Response.HasStarted) return Task.CompletedTask;

        problem.RequestId = httpContext.TraceIdentifier;
        httpContext.Response.StatusCode = problem.Status!.Value;

        var serializerOptions = httpContext.RequestServices
            .GetRequiredService<IOptions<JsonOptions>>()
            .Value.SerializerOptions;

        return httpContext.Response.WriteAsJsonAsync(problem, serializerOptions,
            contentType: MediaTypeNames.Application.ProblemJson);
    }

    /// <summary>
    /// Maps every element of a JSON array claim, rather than the first one the default action finds.
    /// </summary>
    private sealed class JsonArrayClaimAction(string claimType)
        : ClaimAction(claimType, ClaimValueTypes.String)
    {
        public override void Run(JsonElement userData, ClaimsIdentity identity, string issuer)
        {
            if (!userData.TryGetProperty(ClaimType, out var value)) return;

            switch (value.ValueKind)
            {
                case JsonValueKind.Array:
                    foreach (var element in value.EnumerateArray())
                    {
                        AddIfString(element, identity, issuer);
                    }
                    break;
                default:
                    AddIfString(value, identity, issuer);
                    break;
            }
        }

        private void AddIfString(JsonElement element, ClaimsIdentity identity, string issuer)
        {
            if (element.ValueKind != JsonValueKind.String) return;
            if (element.GetString() is not { Length: > 0 } text) return;

            // Userinfo repeats what the id token already provided, so skip duplicates rather than
            // doubling every group in the principal.
            if (identity.HasClaim(ClaimType, text)) return;

            identity.AddClaim(new Claim(ClaimType, text, ValueType, issuer));
        }
    }
}
