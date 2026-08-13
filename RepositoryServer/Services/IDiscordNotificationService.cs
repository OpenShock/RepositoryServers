namespace OpenShock.RepositoryServer.Services;

/// <summary>
/// Fire-and-forget Discord webhook notifications. All methods MUST swallow errors and
/// never fail the triggering operation (see firmware-api-spec.md §12). Implementations
/// return <see cref="Task.CompletedTask"/> when no webhooks are configured.
/// </summary>
public interface IDiscordNotificationService
{
    Task NotifyFirmwareReleasePublishedAsync(string version, string channel, string commitHash, CancellationToken ct);
    Task NotifyDesktopModuleVersionPublishedAsync(string moduleId, string version, string? commitHash, CancellationToken ct);
    Task NotifyReleaseNotesNeedEditingAsync(Guid releaseId, string version, string channel, CancellationToken ct);
    Task NotifyStagedReleaseExpiredAsync(Guid releaseId, string version, string channel, CancellationToken ct);
}
