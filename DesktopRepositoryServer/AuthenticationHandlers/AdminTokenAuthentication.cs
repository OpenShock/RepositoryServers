using System.Net.Mime;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.Options;
using OpenShock.Desktop.RepositoryServer.Config;
using OpenShock.Desktop.RepositoryServer.Errors;
using OpenShock.Desktop.RepositoryServer.Problems;

namespace OpenShock.Desktop.RepositoryServer.AuthenticationHandlers;

public sealed class AdminTokenAuthentication : AuthenticationHandler<AuthenticationSchemeOptions>
{
    private readonly ApiConfig _apiConfig;
    private readonly JsonSerializerOptions _serializerOptions;
    private OpenShockProblem? _authResultError = null;

    public AdminTokenAuthentication(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IOptions<JsonOptions> jsonOptions,
        ApiConfig apiConfig)
        : base(options, logger, encoder)
    {
        _apiConfig = apiConfig;
        _serializerOptions = jsonOptions.Value.SerializerOptions;
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Context.Request.Headers.Authorization.Equals(_apiConfig.AdminToken))
        {
            return Task.FromResult(Fail(AuthResultError.TokenInvalid));
        }

        var ident = new ClaimsIdentity([
            new Claim(ClaimTypes.AuthenticationMethod, AuthSchemas.AdminToken)
        ], nameof(AdminTokenAuthentication));

        Context.User = new ClaimsPrincipal(ident);

        var ticket = new AuthenticationTicket(new ClaimsPrincipal(ident), Scheme.Name);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }

    private AuthenticateResult Fail(OpenShockProblem reason)
    {
        _authResultError = reason;
        return AuthenticateResult.Fail(reason.Type!);
    }

    /// <inheritdoc />
    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        if (Context.Response.HasStarted) return Task.CompletedTask;
        _authResultError ??= AuthResultError.UnknownError;
        Response.StatusCode = _authResultError.Status!.Value;
        _authResultError.AddContext(Context);
        return Context.Response.WriteAsJsonAsync(_authResultError, _serializerOptions,
            contentType: MediaTypeNames.Application.ProblemJson);
    }
}