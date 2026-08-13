using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;

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
    /// replaces that scheme with a stub, so what is asserted here is the refusal itself rather than the
    /// shape of the challenge.
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

        var body = await response.Content.ReadAsStringAsync();
        await Assert.That(body).DoesNotContain("USB devices");
    }

    /// <summary>
    /// Authenticating with Authentik is not the same as being an administrator. A session for an
    /// account outside the configured group has to be refused, otherwise every user in the directory
    /// would inherit admin rights the moment SSO was switched on.
    /// </summary>
    [Test]
    public async Task AdminPage_SessionOutsideAdminGroup_IsRefused()
    {
        using var client = Factory.CreateAdminClient(group: "some-other-group");

        var response = await client.GetAsync(AdminPath);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);

        var body = await response.Content.ReadAsStringAsync();
        await Assert.That(body).DoesNotContain("USB devices");
    }

    [Test]
    public async Task AdminPage_WithAdminSession_Renders()
    {
        using var client = Factory.CreateAdminClient();

        var response = await client.GetAsync(AdminPath);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        var body = await response.Content.ReadAsStringAsync();
        await Assert.That(body).Contains("USB devices");
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
        await Assert.That(body.Groups).Contains(TestAdminAuthHandler.AdminGroup);
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

    private sealed record MeResponse(string? Subject, string? Username, string[] Groups);
}
