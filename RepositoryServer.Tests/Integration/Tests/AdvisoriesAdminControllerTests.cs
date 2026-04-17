using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using OpenShock.RepositoryServer.Models.Firmware;
using OpenShock.RepositoryServer.RepoServerDb;

namespace OpenShock.RepositoryServer.Tests.Integration.Tests;

[NotInParallel("repo-server-integration")]
public class AdvisoriesAdminControllerTests
{
    private const string BasePath = "/v2/firmware/admin/advisories";

    [ClassDataSource<WebApplicationFactory>(Shared = SharedType.PerTestSession)]
    public required WebApplicationFactory Factory { get; init; }

    [Before(Test)]
    public Task Setup() => Factory.ResetDatabaseAsync();

    [Test]
    public async Task Post_ValidRequest_Returns201WithGeneratedId()
    {
        using var client = Factory.CreateAdminClient();

        var response = await client.PostAsJsonAsync(BasePath, new UpsertFirmwareAdvisoryRequest
        {
            Severity = "critical",
            Title = "OTA bricking bug",
            Content = "Versions before 1.4.0 have a bug.",
            AffectedVersions = "<1.4.0",
            Url = "https://example.invalid/issues/123"
        });

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Created);

        var body = await response.Content.ReadFromJsonAsync<FirmwareAdvisoryAdminDto>();
        await Assert.That(body).IsNotNull();
        await Assert.That(body!.Id).IsNotEqualTo(Guid.Empty);
        await Assert.That(body.Severity).IsEqualTo("critical");
        await Assert.That(body.Title).IsEqualTo("OTA bricking bug");
    }

    [Test]
    public async Task Post_InvalidSeverity_Returns400()
    {
        using var client = Factory.CreateAdminClient();

        var response = await client.PostAsJsonAsync(BasePath, new UpsertFirmwareAdvisoryRequest
        {
            Severity = "catastrophic",
            Title = "Nope",
            Content = "Nope",
            AffectedVersions = "<1.0.0"
        });

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task Post_WithoutAuth_Returns401()
    {
        using var client = Factory.CreateClient();

        var response = await client.PostAsJsonAsync(BasePath, new UpsertFirmwareAdvisoryRequest
        {
            Severity = "info",
            Title = "Whatever",
            Content = "Whatever",
            AffectedVersions = ">=1.0.0"
        });

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task Get_ReturnsAllAdvisoriesOrderedBySeverity()
    {
        await SeedAdvisoryAsync(AdvisorySeverity: "info", "Minor issue");
        await SeedAdvisoryAsync(AdvisorySeverity: "critical", "Bricking bug");
        await SeedAdvisoryAsync(AdvisorySeverity: "warning", "Gotcha");

        using var client = Factory.CreateAdminClient();
        var response = await client.GetAsync(BasePath);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<List<FirmwareAdvisoryAdminDto>>();
        await Assert.That(body).IsNotNull();
        await Assert.That(body!).HasCount(3);

        // Ordered by the PG enum ordinal: Critical (0), Warning (1), Info (2).
        await Assert.That(body[0].Severity).IsEqualTo("critical");
        await Assert.That(body[1].Severity).IsEqualTo("warning");
        await Assert.That(body[2].Severity).IsEqualTo("info");
    }

    [Test]
    public async Task Put_ExistingAdvisory_UpdatesFields()
    {
        var id = await SeedAdvisoryAsync(AdvisorySeverity: "info", "Original title");

        using var client = Factory.CreateAdminClient();
        var response = await client.PutAsJsonAsync($"{BasePath}/{id}", new UpsertFirmwareAdvisoryRequest
        {
            Severity = "warning",
            Title = "Updated title",
            Content = "Updated content",
            AffectedVersions = ">=1.0.0 <2.0.0",
            Url = "https://example.invalid/new"
        });

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        var updated = await response.Content.ReadFromJsonAsync<FirmwareAdvisoryAdminDto>();
        await Assert.That(updated).IsNotNull();
        await Assert.That(updated!.Id).IsEqualTo(id);
        await Assert.That(updated.Severity).IsEqualTo("warning");
        await Assert.That(updated.Title).IsEqualTo("Updated title");
    }

    [Test]
    public async Task Put_UnknownId_Returns404()
    {
        using var client = Factory.CreateAdminClient();
        var response = await client.PutAsJsonAsync($"{BasePath}/{Guid.NewGuid()}",
            new UpsertFirmwareAdvisoryRequest
            {
                Severity = "info",
                Title = "Nothing to update",
                Content = "-",
                AffectedVersions = ">=0.0.0"
            });

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task Delete_ExistingAdvisory_Returns204()
    {
        var id = await SeedAdvisoryAsync(AdvisorySeverity: "warning", "Will be deleted");

        using var client = Factory.CreateAdminClient();
        var response = await client.DeleteAsync($"{BasePath}/{id}");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NoContent);

        // Confirm gone via GET
        var list = await client.GetFromJsonAsync<List<FirmwareAdvisoryAdminDto>>(BasePath);
        await Assert.That(list).IsNotNull();
        await Assert.That(list!.Any(a => a.Id == id)).IsFalse();
    }

    [Test]
    public async Task Delete_UnknownId_Returns404()
    {
        using var client = Factory.CreateAdminClient();
        var response = await client.DeleteAsync($"{BasePath}/{Guid.NewGuid()}");
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task Post_Advisory_AppearsOnPublicManifest()
    {
        using var adminClient = Factory.CreateAdminClient();
        var createResponse = await adminClient.PostAsJsonAsync(BasePath, new UpsertFirmwareAdvisoryRequest
        {
            Severity = "info",
            Title = "FYI",
            Content = "Something to know",
            AffectedVersions = ">=1.0.0"
        });
        await Assert.That(createResponse.StatusCode).IsEqualTo(HttpStatusCode.Created);

        using var publicClient = Factory.CreateClient();
        var manifest = await publicClient.GetFromJsonAsync<JsonElement>("/v2/firmware/manifest");
        var advisories = manifest.GetProperty("advisories").EnumerateArray().ToList();

        await Assert.That(advisories).HasCount(1);
        await Assert.That(advisories[0].GetProperty("title").GetString()).IsEqualTo("FYI");
        // Manifest DTO does not expose id — check the fields that are public.
        await Assert.That(advisories[0].TryGetProperty("id", out _)).IsFalse();
    }

    private async Task<Guid> SeedAdvisoryAsync(string AdvisorySeverity, string title)
    {
        using var client = Factory.CreateAdminClient();
        var response = await client.PostAsJsonAsync(BasePath, new UpsertFirmwareAdvisoryRequest
        {
            Severity = AdvisorySeverity,
            Title = title,
            Content = "seed",
            AffectedVersions = ">=0.0.0"
        });
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<FirmwareAdvisoryAdminDto>();
        return body!.Id;
    }
}
