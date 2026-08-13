using Microsoft.EntityFrameworkCore;
using OneOf;
using OneOf.Types;
using OpenShock.RepositoryServer.Enums;
using OpenShock.RepositoryServer.RepoServerDb;
using OpenShock.RepositoryServer.RepoServerDb.Models;

namespace OpenShock.RepositoryServer.Services.Admin;

/// <summary>
/// Discord notification targets. Not firmware-specific: desktop module publishing notifies through
/// the same webhooks.
/// </summary>
/// <remarks>
/// A webhook URL is a credential, since anyone holding it can post to that channel. Callers that
/// display these rows must show the masked form, never <see cref="DiscordWebhook.Url"/> itself.
/// </remarks>
public sealed class DiscordWebhookAdminService
{
    private readonly RepoServerContext _db;

    public DiscordWebhookAdminService(RepoServerContext db)
    {
        _db = db;
    }

    public Task<DiscordWebhook[]> ListAsync(CancellationToken ct = default) =>
        _db.DiscordWebhooks.OrderBy(w => w.Name).ToArrayAsync(ct);

    public Task<DiscordWebhook?> FindAsync(Guid id, CancellationToken ct = default) =>
        _db.DiscordWebhooks.FirstOrDefaultAsync(w => w.Id == id, ct);

    public async Task<DiscordWebhook> CreateAsync(
        string name, string url, DiscordNotificationEvent[] events, bool enabled,
        CancellationToken ct = default)
    {
        var webhook = new DiscordWebhook
        {
            Id = Guid.NewGuid(),
            Name = name,
            Url = url,
            Events = events.Distinct().ToArray(),
            Enabled = enabled
        };

        _db.DiscordWebhooks.Add(webhook);
        await _db.SaveChangesAsync(ct);

        return webhook;
    }

    /// <summary>
    /// Updates a webhook. A null <paramref name="url"/> leaves the stored URL alone, so an editor that
    /// never displays the credential can still save the other fields without erasing it.
    /// </summary>
    public async Task<OneOf<Success, NotFound>> UpdateAsync(
        Guid webhookId, string name, string? url, DiscordNotificationEvent[] events, bool enabled,
        CancellationToken ct = default)
    {
        var webhook = await _db.DiscordWebhooks.FirstOrDefaultAsync(w => w.Id == webhookId, ct);
        if (webhook is null)
        {
            return new NotFound();
        }

        webhook.Name = name;
        webhook.Events = events.Distinct().ToArray();
        webhook.Enabled = enabled;
        if (!string.IsNullOrWhiteSpace(url))
        {
            webhook.Url = url;
        }

        await _db.SaveChangesAsync(ct);
        return new Success();
    }

    public async Task<OneOf<Success, NotFound>> DeleteAsync(Guid webhookId, CancellationToken ct = default)
    {
        var deleted = await _db.DiscordWebhooks.Where(w => w.Id == webhookId).ExecuteDeleteAsync(ct);
        if (deleted <= 0)
        {
            return new NotFound();
        }

        return new Success();
    }
}
