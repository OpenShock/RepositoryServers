using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OpenShock.RepositoryServer.Enums;
using OpenShock.RepositoryServer.Models.Discord;
using OpenShock.RepositoryServer.Problems;
using OpenShock.RepositoryServer.RepoServerDb;
using OpenShock.RepositoryServer.Utils;

namespace OpenShock.RepositoryServer.Controllers.V2.Admin;

/// <summary>
/// Manages Discord notification targets.
/// </summary>
/// <remarks>
/// Not under <c>/firmware/admin</c> because these are not firmware-specific — desktop module
/// publishing notifies through the same webhooks.
///
/// A webhook URL is a credential: anyone holding it can post to that channel. It is therefore
/// write-only. Responses carry a masked form that keeps the Discord webhook id (useful for matching a
/// row to a channel) and drops the token.
/// </remarks>
[ApiVersion("2.0")]
[ApiController]
[Route("/v{version:apiVersion}/admin/discord-webhooks")]
[Authorize(AuthenticationSchemes = AuthSchemas.AdminToken)]
public class DiscordWebhooksController : OpenShockControllerBase
{
    private readonly RepoServerContext _db;

    public DiscordWebhooksController(RepoServerContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<IActionResult> ListWebhooks(CancellationToken ct)
    {
        var rows = await _db.DiscordWebhooks.OrderBy(w => w.Name).ToListAsync(ct);
        return Ok(rows.Select(DiscordWebhookDto.From));
    }

    [HttpPost]
    public async Task<IActionResult> CreateWebhook(
        [FromBody] UpsertDiscordWebhookRequest request,
        CancellationToken ct)
    {
        if (!TryParseEvents(request.Events, out var events, out var invalid))
        {
            return Problem(DiscordError.DiscordInvalidNotificationEvent(invalid));
        }

        var webhook = new DiscordWebhook
        {
            Id = Guid.NewGuid(),
            Name = request.Name,
            Url = request.Url,
            Events = events,
            Enabled = request.Enabled
        };

        _db.DiscordWebhooks.Add(webhook);
        await _db.SaveChangesAsync(ct);

        return Created((string?)null, DiscordWebhookDto.From(webhook));
    }

    [HttpPut("{webhookId:guid}")]
    public async Task<IActionResult> UpdateWebhook(
        [FromRoute] Guid webhookId,
        [FromBody] UpsertDiscordWebhookRequest request,
        CancellationToken ct)
    {
        if (!TryParseEvents(request.Events, out var events, out var invalid))
        {
            return Problem(DiscordError.DiscordInvalidNotificationEvent(invalid));
        }

        var webhook = await _db.DiscordWebhooks.FirstOrDefaultAsync(w => w.Id == webhookId, ct);
        if (webhook is null)
        {
            return Problem(DiscordError.DiscordWebhookNotFound);
        }

        webhook.Name = request.Name;
        webhook.Url = request.Url;
        webhook.Events = events;
        webhook.Enabled = request.Enabled;

        await _db.SaveChangesAsync(ct);
        return Ok(DiscordWebhookDto.From(webhook));
    }

    [HttpDelete("{webhookId:guid}")]
    public async Task<IActionResult> DeleteWebhook([FromRoute] Guid webhookId, CancellationToken ct)
    {
        var deleted = await _db.DiscordWebhooks.Where(w => w.Id == webhookId).ExecuteDeleteAsync(ct);
        if (deleted <= 0)
        {
            return Problem(DiscordError.DiscordWebhookNotFound);
        }

        return NoContent();
    }

    private static bool TryParseEvents(
        List<string>? raw, out DiscordNotificationEvent[] events, out string invalid)
    {
        var parsed = new List<DiscordNotificationEvent>();
        foreach (var value in raw ?? [])
        {
            if (!DiscordNotificationEventExtensions.TryParseEvent(value, out var parsedEvent))
            {
                events = [];
                invalid = value;
                return false;
            }
            if (!parsed.Contains(parsedEvent)) parsed.Add(parsedEvent);
        }

        events = parsed.ToArray();
        invalid = string.Empty;
        return true;
    }
}
