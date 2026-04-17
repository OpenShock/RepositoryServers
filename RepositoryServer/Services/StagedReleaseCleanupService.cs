using Microsoft.EntityFrameworkCore;
using OpenShock.RepositoryServer.Config;
using OpenShock.RepositoryServer.Enums;
using OpenShock.RepositoryServer.RepoServerDb;
using OpenShock.RepositoryServer.Utils;

namespace OpenShock.RepositoryServer.Services;

/// <summary>
/// Periodic cleanup of expired staged firmware releases. Runs every 5 minutes. Two TTLs:
/// <c>StagedReleaseTtl</c> (default 1h) for abandoned CI releases, <c>EditingReleaseTtl</c>
/// (default 7d) for releases waiting on janitor review. See firmware-api-spec.md §5.4.
/// </summary>
public sealed class StagedReleaseCleanupService : BackgroundService
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromMinutes(5);

    private readonly IDbContextFactory<RepoServerContext> _dbFactory;
    private readonly IStorageService _storage;
    private readonly ApiConfig _apiConfig;
    private readonly IDiscordNotificationService _discord;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<StagedReleaseCleanupService> _logger;

    public StagedReleaseCleanupService(
        IDbContextFactory<RepoServerContext> dbFactory,
        IStorageService storage,
        ApiConfig apiConfig,
        IDiscordNotificationService discord,
        TimeProvider timeProvider,
        ILogger<StagedReleaseCleanupService> logger)
    {
        _dbFactory = dbFactory;
        _storage = storage;
        _apiConfig = apiConfig;
        _discord = discord;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TickInterval, _timeProvider);

        // Fire once immediately, then on every tick. The DB may be unreachable at
        // startup (migrations still running, Postgres warming up in tests, etc.) —
        // errors are swallowed so the service keeps retrying on the timer.
        await SafeTickAsync(stoppingToken);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            if (stoppingToken.IsCancellationRequested) return;
            await SafeTickAsync(stoppingToken);
        }
    }

    private async Task SafeTickAsync(CancellationToken ct)
    {
        try
        {
            await TickAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // shutting down — no-op
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Staged-release cleanup tick failed");
        }
    }

    private async Task TickAsync(CancellationToken ct)
    {
        var now = _timeProvider.GetUtcNow();
        var stagedDeadline = now - _apiConfig.Firmware.StagedReleaseTtl;
        var editingDeadline = now - _apiConfig.Firmware.EditingReleaseTtl;

        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var expired = await db.FirmwareReleases
            .Include(r => r.StagedArtifacts)
            .Where(r =>
                (r.Status == ReleaseStatus.Staging && r.CreatedAt < stagedDeadline) ||
                (r.Status == ReleaseStatus.Editing && r.CreatedAt < editingDeadline))
            .ToListAsync(ct);

        if (expired.Count == 0) return;

        foreach (var release in expired)
        {
            try
            {
                foreach (var artifact in release.StagedArtifacts)
                {
                    var cdnFileName = FirmwareArtifactFileNames.GetFileName(artifact.ArtifactType);
                    var cdnPath = $"{release.Version}/{artifact.BoardId}/{cdnFileName}";
                    await _storage.DeleteFileAsync(cdnPath, ct);
                }

                release.Status = ReleaseStatus.Aborted;
                await db.SaveChangesAsync(ct);

                _logger.LogInformation(
                    "Aborted expired staged release {ReleaseId} (version={Version} status={Status}) after TTL expiry",
                    release.Id, release.Version, release.Status);

                await _discord.NotifyStagedReleaseExpiredAsync(
                    release.Id,
                    release.Version,
                    release.Channel.ToString().ToLowerInvariant(),
                    ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to clean up expired staged release {ReleaseId}", release.Id);
            }
        }
    }
}
