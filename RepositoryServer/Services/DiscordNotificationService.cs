using System.Text.Json;
using OpenShock.RepositoryServer.Config;

namespace OpenShock.RepositoryServer.Services;

public sealed class DiscordNotificationService : IDiscordNotificationService
{
    private const int ColorGreen = 0x2ECC71;
    private const int ColorYellow = 0xF1C40F;
    private const int ColorRed = 0xE74C3C;

    private readonly HttpClient _http;
    private readonly DiscordConfig _config;
    private readonly ILogger<DiscordNotificationService> _logger;

    public DiscordNotificationService(HttpClient http, ApiConfig apiConfig, ILogger<DiscordNotificationService> logger)
    {
        _http = http;
        _config = apiConfig.Discord;
        _logger = logger;
    }

    public Task NotifyFirmwareReleasePublishedAsync(string version, string channel, string commitHash, CancellationToken ct) =>
        PostEmbedsAsync("Firmware release published",
            $"Version `{version}` published on channel `{channel}`.\nCommit: `{commitHash[..Math.Min(7, commitHash.Length)]}`",
            ColorGreen, ct);

    public Task NotifyDesktopModuleVersionPublishedAsync(string moduleId, string version, string? commitHash, CancellationToken ct) =>
        PostEmbedsAsync("Desktop module version published",
            $"Module `{moduleId}` version `{version}` published." +
            (!string.IsNullOrEmpty(commitHash) ? $"\nCommit: `{commitHash[..Math.Min(7, commitHash.Length)]}`" : string.Empty),
            ColorGreen, ct);

    public Task NotifyReleaseNotesNeedEditingAsync(Guid releaseId, string version, string channel, CancellationToken ct) =>
        PostEmbedsAsync("Release notes need editing",
            $"Release `{releaseId}` (version `{version}`, channel `{channel}`) was created with an invalid changelog and needs manual review.",
            ColorYellow, ct);

    public Task NotifyStagedReleaseExpiredAsync(Guid releaseId, string version, string channel, CancellationToken ct) =>
        PostEmbedsAsync("Staged release expired",
            $"Release `{releaseId}` (version `{version}`, channel `{channel}`) was aborted because its TTL expired.",
            ColorRed, ct);

    private Task PostEmbedsAsync(string title, string description, int color, CancellationToken ct)
    {
        if (_config.WebhookUrls.Count == 0)
        {
            return Task.CompletedTask;
        }

        var payload = JsonSerializer.Serialize(new
        {
            embeds = new object[]
            {
                new { title, description, color }
            }
        });

        foreach (var url in _config.WebhookUrls)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    using var request = new HttpRequestMessage(HttpMethod.Post, url)
                    {
                        Content = new StringContent(payload, System.Text.Encoding.UTF8, "application/json")
                    };
                    using var response = await _http.SendAsync(request, ct);
                    if (!response.IsSuccessStatusCode)
                    {
                        _logger.LogWarning("Discord webhook returned {Status}", (int)response.StatusCode);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to post Discord notification");
                }
            }, ct);
        }

        return Task.CompletedTask;
    }
}
