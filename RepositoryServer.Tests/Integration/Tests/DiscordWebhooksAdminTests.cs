using Microsoft.EntityFrameworkCore;
using OneOf.Types;
using OpenShock.RepositoryServer.Enums;
using OpenShock.RepositoryServer.Services.Admin;
using OpenShock.RepositoryServer.Utils;

namespace OpenShock.RepositoryServer.Tests.Integration.Tests;

[NotInParallel("repo-server-integration")]
public class DiscordWebhooksAdminTests
{
    private const string WebhookUrl = "https://discord.com/api/webhooks/123456789/s3cr3t-token-value";

    [ClassDataSource<WebApplicationFactory>(Shared = SharedType.PerTestSession)]
    public required WebApplicationFactory Factory { get; init; }

    [Before(Test)]
    public Task Setup() => Factory.ResetDatabaseAsync();

    private Task<T> Webhooks<T>(Func<DiscordWebhookAdminService, Task<T>> operation) =>
        Factory.UseAsync(operation);

    [Test]
    public async Task Create_StoresWebhook()
    {
        var webhook = await Webhooks(s => s.CreateAsync(
            "releases", WebhookUrl, [DiscordNotificationEvent.FirmwareReleasePublished], true));

        await Assert.That(webhook.Id).IsNotEqualTo(Guid.Empty);
        await Assert.That(webhook.Name).IsEqualTo("releases");
        await Assert.That(webhook.Enabled).IsTrue();
        await Assert.That(webhook.Events).Contains(DiscordNotificationEvent.FirmwareReleasePublished);
    }

    /// <summary>
    /// The URL is a credential: whoever holds it can post to that channel. Callers display the masked
    /// form, which keeps the webhook id useful for matching a row to a channel and drops the token.
    /// </summary>
    [Test]
    public async Task MaskedForm_KeepsTheIdAndDropsTheToken()
    {
        var webhook = await Webhooks(s => s.CreateAsync("releases", WebhookUrl, [], true));

        var masked = DiscordWebhookMasking.Mask(webhook.Url);

        await Assert.That(masked).DoesNotContain("s3cr3t-token-value");
        await Assert.That(masked).Contains("123456789");
    }

    [Test]
    public async Task Update_ChangesEventSubscription()
    {
        var webhook = await Webhooks(s => s.CreateAsync(
            "releases", WebhookUrl, [DiscordNotificationEvent.FirmwareReleasePublished], true));

        var result = await Webhooks(s => s.UpdateAsync(
            webhook.Id,
            "releases",
            WebhookUrl,
            [DiscordNotificationEvent.StagedReleaseExpired, DiscordNotificationEvent.ReleaseNotesNeedEditing],
            false));
        result.ShouldBe<Success>();

        var stored = await Factory.UseDbAsync(db => db.DiscordWebhooks.FirstAsync(w => w.Id == webhook.Id));
        await Assert.That(stored.Enabled).IsFalse();
        await Assert.That(stored.Events).Contains(DiscordNotificationEvent.StagedReleaseExpired);
        await Assert.That(stored.Events).DoesNotContain(DiscordNotificationEvent.FirmwareReleasePublished);
    }

    /// <summary>
    /// An editor that never displays the credential has to be able to save the other fields without
    /// wiping it, so a null url means "leave it alone" rather than "clear it".
    /// </summary>
    [Test]
    public async Task Update_WithoutUrl_KeepsTheStoredCredential()
    {
        var webhook = await Webhooks(s => s.CreateAsync("releases", WebhookUrl, [], true));

        var result = await Webhooks(s => s.UpdateAsync(webhook.Id, "renamed", null, [], true));
        result.ShouldBe<Success>();

        var stored = await Factory.UseDbAsync(db => db.DiscordWebhooks.FirstAsync(w => w.Id == webhook.Id));
        await Assert.That(stored.Name).IsEqualTo("renamed");
        await Assert.That(stored.Url).IsEqualTo(WebhookUrl);
    }

    [Test]
    public async Task Update_UnknownId_ReportsNotFound()
    {
        var result = await Webhooks(s => s.UpdateAsync(Guid.NewGuid(), "nope", WebhookUrl, [], true));

        result.ShouldBe<NotFound>();
    }

    [Test]
    public async Task Delete_RemovesWebhook()
    {
        var webhook = await Webhooks(s => s.CreateAsync("releases", WebhookUrl, [], true));

        var result = await Webhooks(s => s.DeleteAsync(webhook.Id));
        result.ShouldBe<Success>();

        var remaining = await Factory.UseDbAsync(db => db.DiscordWebhooks.CountAsync());
        await Assert.That(remaining).IsEqualTo(0);
    }

    [Test]
    public async Task Delete_UnknownId_ReportsNotFound()
    {
        var result = await Webhooks(s => s.DeleteAsync(Guid.NewGuid()));

        result.ShouldBe<NotFound>();
    }

    [Test]
    public async Task StoredRows_RoundTripEventsAndEnabledFlag()
    {
        var webhook = await Webhooks(s => s.CreateAsync(
            "maintainers",
            WebhookUrl,
            [DiscordNotificationEvent.ReleaseNotesNeedEditing, DiscordNotificationEvent.StagedReleaseExpired],
            false));

        var stored = await Factory.UseDbAsync(db => db.DiscordWebhooks.FirstAsync(w => w.Id == webhook.Id));

        await Assert.That(stored.Url).IsEqualTo(WebhookUrl);
        await Assert.That(stored.Enabled).IsFalse();
        await Assert.That(stored.Events.Length).IsEqualTo(2);
    }

    [Test]
    public async Task List_IsOrderedByName()
    {
        await Webhooks(s => s.CreateAsync("zeta", WebhookUrl, [], true));
        await Webhooks(s => s.CreateAsync("alpha", WebhookUrl, [], true));

        var all = await Webhooks(s => s.ListAsync());

        await Assert.That(all.Select(w => w.Name).ToArray()).IsEquivalentTo(new[] { "alpha", "zeta" });
    }
}
