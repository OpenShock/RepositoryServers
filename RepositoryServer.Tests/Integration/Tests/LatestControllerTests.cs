using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using OpenShock.RepositoryServer.Enums;
using OpenShock.RepositoryServer.RepoServerDb;

namespace OpenShock.RepositoryServer.Tests.Integration.Tests;

[NotInParallel("repo-server-integration")]
public class LatestControllerTests
{
    /// <summary>Slug-form board name, matching what a hub compiles in as OPENSHOCK_FW_BOARD.</summary>
    private const string BoardName = "Wemos-D1-Mini-ESP32";

    [ClassDataSource<WebApplicationFactory>(Shared = SharedType.PerTestSession)]
    public required WebApplicationFactory Factory { get; init; }

    [Before(Test)]
    public Task Setup() => Factory.ResetDatabaseAsync();

    [Test]
    public async Task GetLatest_InvalidChannel_Returns400()
    {
        using var client = Factory.CreateClient();
        var response = await client.GetAsync("/2/firmware/latest/nightly");
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task GetLatest_ChannelWithNoReleases_Returns404()
    {
        using var client = Factory.CreateClient();
        var response = await client.GetAsync("/2/firmware/latest/stable");
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task GetLatest_HappyPath_ReturnsReleaseWithSourceAndBoards()
    {
        var (boardId, _, _) = await SeedReleaseAsync("1.5.1", ReleaseChannel.Stable);

        using var client = Factory.CreateClient();
        var response = await client.GetAsync("/2/firmware/latest/stable");
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        await Assert.That(body.GetProperty("version").GetString()).IsEqualTo("1.5.1");
        await Assert.That(body.GetProperty("channel").GetString()).IsEqualTo("stable");

        var source = body.GetProperty("source");
        await Assert.That(source.GetProperty("repository").GetProperty("provider").GetString())
            .IsEqualTo("github");
        await Assert.That(source.GetProperty("commitUrl").GetString())
            .IsEqualTo("https://github.com/openshock/firmware/commit/abc1234567890abcdef1234567890abcdef12345");

        var boards = body.GetProperty("boards");
        var boardKeys = boards.EnumerateObject().Select(p => p.Name).ToList();
        await Assert.That(boardKeys).Contains(BoardName);
    }

    [Test]
    public async Task GetLatestForBoard_UnknownChannel_Returns400()
    {
        using var client = Factory.CreateClient();
        var response = await client.GetAsync($"/2/firmware/latest/nightly/{Guid.NewGuid()}");
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task GetLatestForBoard_NoReleases_Returns404()
    {
        using var client = Factory.CreateClient();
        var response = await client.GetAsync($"/2/firmware/latest/stable/{Guid.NewGuid()}");
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task GetLatestForBoard_MatchingVersion_Returns204()
    {
        var (boardId, _, _) = await SeedReleaseAsync("1.5.1", ReleaseChannel.Stable);

        using var client = Factory.CreateClient();
        var response = await client.GetAsync($"/2/firmware/latest/stable/{boardId}?version=1.5.1");
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
    }

    [Test]
    public async Task GetLatestForBoard_DifferentVersion_Returns200WithArtifacts()
    {
        var (boardId, _, _) = await SeedReleaseAsync("1.5.1", ReleaseChannel.Stable);

        using var client = Factory.CreateClient();
        var response = await client.GetAsync($"/2/firmware/latest/stable/{boardId}?version=1.4.0");
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        await Assert.That(body.GetProperty("version").GetString()).IsEqualTo("1.5.1");
        await Assert.That(body.GetProperty("boardId").GetString()).IsEqualTo(BoardName);
        await Assert.That(body.GetProperty("artifacts").GetArrayLength()).IsEqualTo(1);
    }

    [Test]
    public async Task GetLatestForBoard_NoVersionParam_Returns200()
    {
        var (boardId, _, _) = await SeedReleaseAsync("1.5.1", ReleaseChannel.Stable);

        using var client = Factory.CreateClient();
        var response = await client.GetAsync($"/2/firmware/latest/stable/{boardId}");
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
    }

    [Test]
    public async Task GetLatestForBoard_ByName_Returns200WithNameEchoed()
    {
        await SeedReleaseAsync("1.5.1", ReleaseChannel.Stable);

        using var client = Factory.CreateClient();
        var response = await client.GetAsync($"/2/firmware/latest/stable/{BoardName}?version=1.4.0");
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        await Assert.That(body.GetProperty("boardId").GetString()).IsEqualTo(BoardName);
    }

    [Test]
    public async Task GetLatestForBoard_ByNameDifferentCase_Returns200WithCanonicalName()
    {
        await SeedReleaseAsync("1.5.1", ReleaseChannel.Stable);

        using var client = Factory.CreateClient();
        var response = await client.GetAsync(
            $"/2/firmware/latest/stable/{BoardName.ToLowerInvariant()}?version=1.4.0");
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        // The canonical stored spelling comes back, not what the caller sent.
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        await Assert.That(body.GetProperty("boardId").GetString()).IsEqualTo(BoardName);
    }

    [Test]
    public async Task GetLatestForBoard_ByName_MatchingVersion_Returns204()
    {
        await SeedReleaseAsync("1.5.1", ReleaseChannel.Stable);

        using var client = Factory.CreateClient();
        var response = await client.GetAsync($"/2/firmware/latest/stable/{BoardName}?version=1.5.1");
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
    }

    [Test]
    public async Task GetLatestForBoard_ArtifactUrlUsesImmutableBoardId()
    {
        var (boardId, _, _) = await SeedReleaseAsync("1.5.1", ReleaseChannel.Stable);

        using var client = Factory.CreateClient();
        var body = await client.GetFromJsonAsync<JsonElement>(
            $"/2/firmware/latest/stable/{BoardName}");

        // Storage paths are keyed by the board id so a rename cannot strand published artifacts,
        // while the response still labels the board by name.
        await Assert.That(body.GetProperty("boardId").GetString()).IsEqualTo(BoardName);

        var url = body.GetProperty("artifacts")[0].GetProperty("url").GetString();
        await Assert.That(url).Contains($"/1.5.1/{boardId}/");
    }

    [Test]
    public async Task GetLatestForBoard_UnknownName_Returns404NotNoContent()
    {
        await SeedReleaseAsync("1.5.1", ReleaseChannel.Stable);

        // An unknown board must not be masked as "already up to date" just because the
        // version query happens to match the latest release.
        using var client = Factory.CreateClient();
        var response = await client.GetAsync("/2/firmware/latest/stable/No-Such-Board?version=1.5.1");
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task GetLatestForBoard_UnknownBoard_Returns404()
    {
        await SeedReleaseAsync("1.5.1", ReleaseChannel.Stable);

        using var client = Factory.CreateClient();
        var response = await client.GetAsync($"/2/firmware/latest/stable/{Guid.NewGuid()}");
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task GetLatest_BetaChannel_IncludesNewerStableRelease()
    {
        // CI advances stable, beta and develop pointers together when it ships a stable build, so a
        // beta subscriber must be offered it. Strict per-channel equality would pin them back to the
        // last explicit release candidate and offer a downgrade.
        var (_, chipId, repoId) = await SeedReleaseAsync("1.5.0-beta.1", ReleaseChannel.Beta);
        await AddVersionAsync("1.5.0", ReleaseChannel.Stable, DateTimeOffset.UtcNow, repoId);

        using var client = Factory.CreateClient();
        var body = await client.GetFromJsonAsync<JsonElement>("/2/firmware/latest/beta");
        await Assert.That(body.GetProperty("version").GetString()).IsEqualTo("1.5.0");
    }

    [Test]
    public async Task GetLatest_StableChannel_ExcludesBetaReleases()
    {
        // The cascade only runs one way.
        var (_, _, repoId) = await SeedReleaseAsync("1.5.0", ReleaseChannel.Stable);
        await AddVersionAsync("1.6.0-beta.1", ReleaseChannel.Beta, DateTimeOffset.UtcNow.AddDays(1), repoId);

        using var client = Factory.CreateClient();
        var body = await client.GetFromJsonAsync<JsonElement>("/2/firmware/latest/stable");
        await Assert.That(body.GetProperty("version").GetString()).IsEqualTo("1.5.0");
    }

    [Test]
    public async Task GetLatest_TiedReleaseDates_ResolvesDeterministicallyAndAgreesWithManifest()
    {
        // release_date is client-supplied and not unique — the spec's own example uses midnight. With
        // no tiebreaker, ties were broken arbitrarily by the database and endpoints could disagree
        // with each other and flip between requests, which a hub sees as firmware flapping.
        var sameInstant = DateTimeOffset.Parse("2026-04-15T00:00:00Z");
        var (_, _, repoId) = await SeedReleaseAsync("1.5.1", ReleaseChannel.Stable, sameInstant);
        await AddVersionAsync("1.5.2", ReleaseChannel.Stable, sameInstant, repoId);

        using var client = Factory.CreateClient();

        var first = await client.GetFromJsonAsync<JsonElement>("/2/firmware/latest/stable");
        var picked = first.GetProperty("version").GetString();
        await Assert.That(picked).IsEqualTo("1.5.2");

        // Stable across repeated calls...
        for (var i = 0; i < 3; i++)
        {
            var again = await client.GetFromJsonAsync<JsonElement>("/2/firmware/latest/stable");
            await Assert.That(again.GetProperty("version").GetString()).IsEqualTo(picked);
        }

        // ...and consistent with the manifest, which computes latest independently.
        var manifest = await client.GetFromJsonAsync<JsonElement>("/2/firmware/manifest");
        await Assert.That(manifest.GetProperty("latest").GetProperty("stable").GetString())
            .IsEqualTo(picked);
    }

    private async Task AddVersionAsync(
        string version, ReleaseChannel channel, DateTimeOffset releaseDate, Guid repoId)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<RepoServerContext>();

        db.FirmwareVersions.Add(new FirmwareVersion
        {
            Version = version,
            Channel = channel,
            ReleaseDate = releaseDate,
            RepositoryId = repoId,
            CommitHash = "abc1234567890abcdef1234567890abcdef12345"
        });
        await db.SaveChangesAsync();
    }

    private async Task<(Guid boardId, Guid chipId, Guid repoId)> SeedReleaseAsync(
        string version, ReleaseChannel channel, DateTimeOffset? releaseDate = null)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<RepoServerContext>();

        var chip = new FirmwareChip
        {
            Id = Guid.NewGuid(),
            Name = "ESP32-S3",
            Architecture = FirmwareChipArchitecture.Xtensa
        };
        var board = new FirmwareBoard
        {
            Id = Guid.NewGuid(),
            Name = BoardName,
            ChipId = chip.Id,
            RequiredArtifactTypes = [FirmwareArtifactType.Merged]
        };
        var repo = new SourceRepository
        {
            Id = Guid.NewGuid(),
            Provider = RepositoryProvider.Github,
            Owner = "openshock",
            Repo = "firmware"
        };
        var fwVersion = new FirmwareVersion
        {
            Version = version,
            Channel = channel,
            ReleaseDate = releaseDate ?? DateTimeOffset.UtcNow,
            RepositoryId = repo.Id,
            CommitHash = "abc1234567890abcdef1234567890abcdef12345",
            Ref = "refs/tags/v" + version
        };
        var artifact = new FirmwareArtifact
        {
            Version = version,
            BoardId = board.Id,
            ArtifactType = FirmwareArtifactType.Merged,
            HashSha256 = new byte[32],
            FileSize = 1_572_864
        };

        db.FirmwareChips.Add(chip);
        db.FirmwareBoards.Add(board);
        db.Repositories.Add(repo);
        db.FirmwareVersions.Add(fwVersion);
        db.FirmwareArtifacts.Add(artifact);
        await db.SaveChangesAsync();

        return (board.Id, chip.Id, repo.Id);
    }
}
