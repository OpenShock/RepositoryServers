using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc.Testing;
using OpenShock.RepositoryServer.AuthenticationHandlers;
using OpenShock.RepositoryServer.Config;

namespace OpenShock.RepositoryServer.Tests.Integration.Tests;

/// <summary>
/// Covers the gate in front of every admin endpoint, rather than any one endpoint's behaviour.
/// </summary>
[NotInParallel("repo-server-integration")]
public class AdminAuthorizationTests
{
    // Administration is the management UI; there is no admin API to point at. These pages carry the
    // policy as endpoint metadata, so authorization is settled before any markup is produced.
    private const string AdminPath = "/admin/firmware/usb-devices";

    [ClassDataSource<WebApplicationFactory>(Shared = SharedType.PerTestSession)]
    public required WebApplicationFactory Factory { get; init; }

    [Before(Test)]
    public Task Setup() => Factory.ResetDatabaseAsync();

    /// <summary>
    /// The point of rendering on the server: an anonymous visitor is turned away at the endpoint, so no
    /// markup is produced at all.
    /// </summary>
    /// <remarks>
    /// In production the cookie handler turns this into a redirect to <c>/auth/login</c>. The harness
    /// replaces that scheme with a stub, so what is asserted here is the refusal itself rather than
    /// the shape of the challenge.
    /// </remarks>
    [Test]
    public async Task AdminPage_WithoutSession_IsRefusedAndRendersNothing()
    {
        using var client = Factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        var response = await client.GetAsync(AdminPath);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);

        // Not one byte of the admin app: no page text, and no shell to boot it from either.
        var body = await response.Content.ReadAsStringAsync();
        await Assert.That(body).DoesNotContain("USB devices");
        await Assert.That(body).DoesNotContain("blazor.web.js");
    }

    /// <summary>
    /// Authenticating with GitHub is not the same as being an administrator. A session for an account
    /// outside the configured team has to be refused, otherwise every GitHub user on earth would
    /// inherit admin rights the moment the OAuth app was created.
    /// </summary>
    [Test]
    public async Task AdminPage_SessionOutsideAdminTeam_IsRefused()
    {
        using var client = Factory.CreateAdminClient(team: "some-other-team");

        var response = await client.GetAsync(AdminPath);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);

        var body = await response.Content.ReadAsStringAsync();
        await Assert.That(body).DoesNotContain("USB devices");
        await Assert.That(body).DoesNotContain("blazor.web.js");
    }

    /// <summary>
    /// The other half of the gate: an admin session gets past it and is served the app.
    /// </summary>
    /// <remarks>
    /// Asserts on the shell rather than on page text. The admin UI renders interactively with
    /// prerendering off, so the response an admin receives is the document plus a component marker,
    /// and the table itself arrives over the circuit afterwards. What this pins down is that the
    /// policy admitted the request — the page content is not in any HTTP response to compare, for
    /// either party.
    /// </remarks>
    [Test]
    public async Task AdminPage_WithAdminSession_IsServedTheApp()
    {
        using var client = Factory.CreateAdminClient();

        var response = await client.GetAsync(AdminPath);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        var body = await response.Content.ReadAsStringAsync();
        await Assert.That(body).Contains("blazor.web.js");
        await Assert.That(body).Contains("Blazor:");
    }

    [Test]
    public async Task Me_WithAdminSession_ReturnsIdentity()
    {
        using var client = Factory.CreateAdminClient(username: "someone@openshock.example");

        var response = await client.GetAsync("/auth/me");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<MeResponse>();
        await Assert.That(body).IsNotNull();
        await Assert.That(body!.Username).IsEqualTo("someone@openshock.example");
        await Assert.That(body.Team).IsEqualTo(TestAdminAuthHandler.AdminTeam);
    }

    [Test]
    public async Task Me_WithoutSession_Returns401()
    {
        using var client = Factory.CreateClient();

        var response = await client.GetAsync("/auth/me");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// A returnUrl pointing off this origin would make the login itself the delivery mechanism for a
    /// redirect to somewhere else, arriving from the genuine host right after a genuine sign-in.
    /// </summary>
    [Test]
    [Arguments("https://evil.example/steal")]
    [Arguments("//evil.example/steal")]
    [Arguments("/\\evil.example")]
    public async Task Login_NonLocalReturnUrl_Returns400(string returnUrl)
    {
        using var client = Factory.CreateClient();

        var response = await client.GetAsync($"/auth/login?returnUrl={Uri.EscapeDataString(returnUrl)}");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
    }

    /// <summary>
    /// The challenge the cookie handler issues has to land somewhere that exists.
    /// </summary>
    /// <remarks>
    /// Left at their framework defaults, <c>LoginPath</c> and <c>ReturnUrlParameter</c> point at
    /// ASP.NET Identity's scaffolding, which this app does not have: browsing to an admin page
    /// redirected to <c>/Account/Login</c> and ended on the 404 page. Driving the request off the
    /// configured options rather than a literal is the point — a login path that stops matching the
    /// controller fails here. The non-local return url makes one assertion cover both halves: only
    /// the login action answers it with a 400, so a wrong path or a query parameter the action does
    /// not bind shows up as a redirect instead.
    /// </remarks>
    [Test]
    public async Task CookieChallenge_TargetsTheLoginEndpoint()
    {
        var options = new CookieAuthenticationOptions();
        GitHubAuthentication.ConfigureCookie(options, new GitHubAuthConfig
        {
            ClientId = "repository-server-tests",
            ClientSecret = "test-client-secret",
            Organization = "OpenShockTests",
            Team = TestAdminAuthHandler.AdminTeam
        });

        using var client = Factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        var response = await client.GetAsync(
            $"{options.LoginPath}?{options.ReturnUrlParameter}={Uri.EscapeDataString("https://evil.example")}");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
    }

    /// <summary>
    /// Signing out has to land somewhere that renders without a session.
    /// </summary>
    /// <remarks>
    /// Follows the redirect rather than requesting the page directly, so what is pinned down is the
    /// whole exit: that logout sends the browser somewhere routed, and that the destination serves an
    /// anonymous visitor. It used to redirect to the unrouted root, which ended on the not-found
    /// page. Asserts on the shell for the same reason as the admin page above — prerendering is off,
    /// so the page text arrives over the circuit rather than in this response.
    /// </remarks>
    [Test]
    public async Task Logout_LandsOnAPageThatRendersWithoutASession()
    {
        using var client = Factory.CreateClient();

        var response = await client.GetAsync("/auth/logout");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(response.RequestMessage!.RequestUri!.AbsolutePath).IsEqualTo("/auth/signed-out");

        var body = await response.Content.ReadAsStringAsync();
        await Assert.That(body).Contains("blazor.web.js");
    }

    private sealed record MeResponse(string? Subject, string? Username, string? Team);
}
