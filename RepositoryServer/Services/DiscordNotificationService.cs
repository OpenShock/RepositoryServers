using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OpenShock.Internal.Common.Utils;
using OpenShock.RepositoryServer.Enums;
using OpenShock.RepositoryServer.RepoServerDb;

namespace OpenShock.RepositoryServer.Services;

public sealed class DiscordNotificationService : IDiscordNotificationService
{
    private const int ColorGreen = 0x2ECC71;
    private const int ColorYellow = 0xF1C40F;
    private const int ColorRed = 0xE74C3C;

    /// <summary>Ceiling on a fire-and-forget delivery, since it no longer inherits a request timeout.</summary>
    private static readonly TimeSpan DeliveryTimeout = TimeSpan.FromSeconds(15);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IDbContextFactory<RepoServerContext> _dbFactory;
    private readonly ILogger<DiscordNotificationService> _logger;

    public DiscordNotificationService(
        IHttpClientFactory httpClientFactory,
        IDbContextFactory<RepoServerContext> dbFactory,
        ILogger<DiscordNotificationService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _dbFactory = dbFactory;
        _logger = logger;
    }

    public Task NotifyFirmwareReleasePublishedAsync(string version, string channel, string commitHash, CancellationToken ct) =>
        DispatchAsync(DiscordNotificationEvent.FirmwareReleasePublished,
            "Firmware release published",
            $"Version `{version}` published on channel `{channel}`.\nCommit: `{commitHash[..Math.Min(7, commitHash.Length)]}`",
            ColorGreen);

    public Task NotifyDesktopModuleVersionPublishedAsync(string moduleId, string version, string? commitHash, CancellationToken ct) =>
        DispatchAsync(DiscordNotificationEvent.DesktopModuleVersionPublished,
            "Desktop module version published",
            $"Module `{moduleId}` version `{version}` published." +
            (!string.IsNullOrEmpty(commitHash) ? $"\nCommit: `{commitHash[..Math.Min(7, commitHash.Length)]}`" : string.Empty),
            ColorGreen);

    public Task NotifyReleaseNotesNeedEditingAsync(Guid releaseId, string version, string channel, CancellationToken ct) =>
        DispatchAsync(DiscordNotificationEvent.ReleaseNotesNeedEditing,
            "Release notes need editing",
            $"Release `{releaseId}` (version `{version}`, channel `{channel}`) was created with an invalid changelog and needs manual review.",
            ColorYellow);

    public Task NotifyStagedReleaseExpiredAsync(Guid releaseId, string version, string channel, CancellationToken ct) =>
        DispatchAsync(DiscordNotificationEvent.StagedReleaseExpired,
            "Staged release expired",
            $"Release `{releaseId}` (version `{version}`, channel `{channel}`) was aborted because its TTL expired.",
            ColorRed);

    /// <summary>
    /// Queues delivery to every enabled webhook subscribed to <paramref name="notificationEvent"/>.
    /// </summary>
    /// <remarks>
    /// Deliberately takes no <see cref="CancellationToken"/> from the caller. This is fire-and-forget
    /// work that outlives the request that triggered it, so inheriting the request's token meant a
    /// notification was dropped the moment the response completed — including the "needs editing"
    /// alert, which is the only signal that a release is stuck. For the same reason it resolves its
    /// own <see cref="HttpClient"/> and <see cref="RepoServerContext"/> rather than capturing
    /// request-scoped ones, which were disposed out from under it.
    /// </remarks>
    private Task DispatchAsync(DiscordNotificationEvent notificationEvent, string title, string description, int color)
    {
        var payload = JsonSerializer.Serialize(new
        {
            embeds = new object[] { new { title, description, color } }
        });

        OsTask.Run(async () =>
        {
            try
            {
                using var timeout = new CancellationTokenSource(DeliveryTimeout);

                await using var db = await _dbFactory.CreateDbContextAsync(timeout.Token);
                var urls = await db.DiscordWebhooks
                    .Where(w => w.Enabled && w.Events.Contains(notificationEvent))
                    .Select(w => w.Url)
                    .ToListAsync(timeout.Token);

                if (urls.Count == 0) return;

                using var http = _httpClientFactory.CreateClient(nameof(DiscordNotificationService));

                foreach (var url in urls)
                {
                    try
                    {
                        using var content = new StringContent(payload, Encoding.UTF8, "application/json");
                        using var response = await http.PostAsync(url, content, timeout.Token);
                        if (!response.IsSuccessStatusCode)
                        {
                            // The URL is a credential, so it is never logged.
                            _logger.LogWarning(
                                "Discord webhook returned {Status} for {Event}",
                                (int)response.StatusCode, notificationEvent);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to post Discord notification for {Event}", notificationEvent);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to dispatch Discord notifications for {Event}", notificationEvent);
            }
        });

        return Task.CompletedTask;
    }
}
