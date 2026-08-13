using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using OpenShock.RepositoryServer.Enums;
using OpenShock.RepositoryServer.Models.Discord;
using OpenShock.RepositoryServer.RepoServerDb;

namespace OpenShock.RepositoryServer.Tests.Integration.Tests;

[NotInParallel("repo-server-integration")]
public class DiscordWebhooksControllerTests
{
    private const string BasePath = "/v2/admin/discord-webhooks";
    private const string WebhookUrl = "https://discord.com/api/webhooks/123456789/s3cr3t-token-value";

    [ClassDataSource<WebApplicationFactory>(Shared = SharedType.PerTestSession)]
    public required WebApplicationFactory Factory { get; init; }

    [Before(Test)]
    public Task Setup() => Factory.ResetDatabaseAsync();

    [Test]
    public async Task Post_CreatesWebhook_AndNeverEchoesTheUrl()
    {
        using var client = Factory.CreateAdminClient();

        var response = await client.PostAsJsonAsync(BasePath, new UpsertDiscordWebhookRequest
        {
            Name = "releases",
            Url = WebhookUrl,
            Events = ["firmware_release_published"]
        });

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Created);

        // The URL is the credential — possession of it is authorization to post to the channel — so it
        // must never come back out, in any field.
        var raw = await response.Content.ReadAsStringAsync();
        await Assert.That(raw).DoesNotContain("s3cr3t-token-value");

        var body = JsonSerializer.Deserialize<JsonElement>(raw);
        await Assert.That(body.GetProperty("urlMasked").GetString()).IsEqualTo(
            "https://discord.com/api/webhooks/123456789/***");
    }

    [Test]
    public async Task Get_ListsWebhooksWithoutLeakingUrls()
    {
        using var client = Factory.CreateAdminClient();
        await client.PostAsJsonAsync(BasePath, new UpsertDiscordWebhookRequest
        {
            Name = "releases", Url = WebhookUrl, Events = ["firmware_release_published"]
        });

        var response = await client.GetAsync(BasePath);
        var raw = await response.Content.ReadAsStringAsync();

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(raw).DoesNotContain("s3cr3t-token-value");
        await Assert.That(raw).Contains("releases");
    }

    [Test]
    public async Task Post_UnknownEvent_Returns400()
    {
        using var client = Factory.CreateAdminClient();

        var response = await client.PostAsJsonAsync(BasePath, new UpsertDiscordWebhookRequest
        {
            Name = "releases", Url = WebhookUrl, Events = ["everything"]
        });

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task Put_UpdatesEventSubscription()
    {
        using var client = Factory.CreateAdminClient();
        var created = await client.PostAsJsonAsync(BasePath, new UpsertDiscordWebhookRequest
        {
            Name = "releases", Url = WebhookUrl, Events = ["firmware_release_published"]
        });
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var response = await client.PutAsJsonAsync($"{BasePath}/{id}", new UpsertDiscordWebhookRequest
        {
            Name = "maintainers",
            Url = WebhookUrl,
            Events = ["release_notes_need_editing", "staged_release_expired"]
        });

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var events = body.GetProperty("events").EnumerateArray().Select(e => e.GetString()).ToList();
        await Assert.That(events).Contains("release_notes_need_editing");
        await Assert.That(events).DoesNotContain("firmware_release_published");
    }

    [Test]
    public async Task Delete_RemovesWebhook()
    {
        using var client = Factory.CreateAdminClient();
        var created = await client.PostAsJsonAsync(BasePath, new UpsertDiscordWebhookRequest
        {
            Name = "releases", Url = WebhookUrl, Events = []
        });
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var response = await client.DeleteAsync($"{BasePath}/{id}");
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NoContent);

        var again = await client.DeleteAsync($"{BasePath}/{id}");
        await Assert.That(again.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task Endpoints_RequireTheAdminToken()
    {
        using var client = Factory.CreateClient();

        var response = await client.GetAsync(BasePath);
        await Assert.That(response.IsSuccessStatusCode).IsFalse();
    }

    [Test]
    public async Task StoredRows_RoundTripEventsAndEnabledFlag()
    {
        using var client = Factory.CreateAdminClient();
        await client.PostAsJsonAsync(BasePath, new UpsertDiscordWebhookRequest
        {
            Name = "maintainers",
            Url = WebhookUrl,
            Events = ["release_notes_need_editing"],
            Enabled = false
        });

        // Verified against the database rather than the response, since the delivery path reads rows.
        await using var scope = Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<RepoServerContext>();
        var row = db.DiscordWebhooks.Single();

        await Assert.That(row.Enabled).IsFalse();
        await Assert.That(row.Events).Contains(DiscordNotificationEvent.ReleaseNotesNeedEditing);
        await Assert.That(row.Url).IsEqualTo(WebhookUrl);
    }
}
