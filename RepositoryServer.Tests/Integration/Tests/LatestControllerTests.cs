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
        var response = await client.GetAsync("/v2/firmware/latest/nightly");
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task GetLatest_ChannelWithNoReleases_Returns404()
    {
        using var client = Factory.CreateClient();
        var response = await client.GetAsync("/v2/firmware/latest/stable");
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task GetLatest_HappyPath_ReturnsReleaseWithSourceAndBoards()
    {
        var (boardId, _, _) = await SeedReleaseAsync("1.5.1", ReleaseChannel.Stable);

        using var client = Factory.CreateClient();
        var response = await client.GetAsync("/v2/firmware/latest/stable");
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
        var response = await client.GetAsync($"/v2/firmware/latest/nightly/{Guid.NewGuid()}");
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task GetLatestForBoard_NoReleases_Returns404()
    {
        using var client = Factory.CreateClient();
        var response = await client.GetAsync($"/v2/firmware/latest/stable/{Guid.NewGuid()}");
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task GetLatestForBoard_MatchingVersion_Returns204()
    {
        var (boardId, _, _) = await SeedReleaseAsync("1.5.1", ReleaseChannel.Stable);

        using var client = Factory.CreateClient();
        var response = await client.GetAsync($"/v2/firmware/latest/stable/{boardId}?version=1.5.1");
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
    }

    [Test]
    public async Task GetLatestForBoard_DifferentVersion_Returns200WithArtifacts()
    {
        var (boardId, _, _) = await SeedReleaseAsync("1.5.1", ReleaseChannel.Stable);

        using var client = Factory.CreateClient();
        var response = await client.GetAsync($"/v2/firmware/latest/stable/{boardId}?version=1.4.0");
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
        var response = await client.GetAsync($"/v2/firmware/latest/stable/{boardId}");
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
    }

    [Test]
    public async Task GetLatestForBoard_ByName_Returns200WithNameEchoed()
    {
        await SeedReleaseAsync("1.5.1", ReleaseChannel.Stable);

        using var client = Factory.CreateClient();
        var response = await client.GetAsync($"/v2/firmware/latest/stable/{BoardName}?version=1.4.0");
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
            $"/v2/firmware/latest/stable/{BoardName.ToLowerInvariant()}?version=1.4.0");
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
        var response = await client.GetAsync($"/v2/firmware/latest/stable/{BoardName}?version=1.5.1");
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
    }

    [Test]
    public async Task GetLatestForBoard_ArtifactUrlUsesImmutableBoardId()
    {
        var (boardId, _, _) = await SeedReleaseAsync("1.5.1", ReleaseChannel.Stable);

        using var client = Factory.CreateClient();
        var body = await client.GetFromJsonAsync<JsonElement>(
            $"/v2/firmware/latest/stable/{BoardName}");

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
        var response = await client.GetAsync("/v2/firmware/latest/stable/No-Such-Board?version=1.5.1");
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task GetLatestForBoard_UnknownBoard_Returns404()
    {
        await SeedReleaseAsync("1.5.1", ReleaseChannel.Stable);

        using var client = Factory.CreateClient();
        var response = await client.GetAsync($"/v2/firmware/latest/stable/{Guid.NewGuid()}");
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }

    private async Task<(Guid boardId, Guid chipId, Guid repoId)> SeedReleaseAsync(
        string version, ReleaseChannel channel)
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
            ReleaseDate = DateTimeOffset.UtcNow,
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
