using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using OpenShock.RepositoryServer.Enums;
using OpenShock.RepositoryServer.RepoServerDb;

namespace OpenShock.RepositoryServer.Tests.Integration.Tests;

[NotInParallel("repo-server-integration")]
public class VersionsControllerTests
{
    [ClassDataSource<WebApplicationFactory>(Shared = SharedType.PerTestSession)]
    public required WebApplicationFactory Factory { get; init; }

    [Before(Test)]
    public Task Setup() => Factory.ResetDatabaseAsync();

    [Test]
    public async Task List_EmptyDatabase_ReturnsZeroTotal()
    {
        using var client = Factory.CreateClient();
        var response = await client.GetAsync("/v2/firmware/versions");
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        await Assert.That(body.GetProperty("total").GetInt32()).IsEqualTo(0);
        await Assert.That(body.GetProperty("versions").GetArrayLength()).IsEqualTo(0);
    }

    [Test]
    public async Task List_WithVersions_OrdersByReleaseDateDescending()
    {
        await SeedVersionsAsync(("1.5.0", ReleaseChannel.Stable, DateTimeOffset.UtcNow.AddDays(-5)),
                                ("1.5.1", ReleaseChannel.Stable, DateTimeOffset.UtcNow.AddDays(-1)),
                                ("1.5.2-beta.1", ReleaseChannel.Beta, DateTimeOffset.UtcNow));

        using var client = Factory.CreateClient();
        var body = await client.GetFromJsonAsync<JsonElement>("/v2/firmware/versions");

        await Assert.That(body.GetProperty("total").GetInt32()).IsEqualTo(3);
        var versions = body.GetProperty("versions").EnumerateArray()
            .Select(v => v.GetProperty("version").GetString()).ToList();
        await Assert.That(versions[0]).IsEqualTo("1.5.2-beta.1");
        await Assert.That(versions[1]).IsEqualTo("1.5.1");
        await Assert.That(versions[2]).IsEqualTo("1.5.0");
    }

    [Test]
    public async Task List_FilterByChannel_ReturnsOnlyMatching()
    {
        await SeedVersionsAsync(
            ("1.5.0", ReleaseChannel.Stable, DateTimeOffset.UtcNow.AddDays(-2)),
            ("1.6.0-beta.1", ReleaseChannel.Beta, DateTimeOffset.UtcNow.AddDays(-1)));

        using var client = Factory.CreateClient();
        var body = await client.GetFromJsonAsync<JsonElement>("/v2/firmware/versions?channel=stable");

        await Assert.That(body.GetProperty("total").GetInt32()).IsEqualTo(1);
        var versions = body.GetProperty("versions").EnumerateArray()
            .Select(v => v.GetProperty("version").GetString()).ToList();
        await Assert.That(versions).HasCount(1);
        await Assert.That(versions[0]).IsEqualTo("1.5.0");
    }

    [Test]
    public async Task List_InvalidChannel_Returns400()
    {
        using var client = Factory.CreateClient();
        var response = await client.GetAsync("/v2/firmware/versions?channel=nightly");
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task List_LimitAndOffset_Paginate()
    {
        await SeedVersionsAsync(
            ("1.0.0", ReleaseChannel.Stable, DateTimeOffset.UtcNow.AddDays(-5)),
            ("1.1.0", ReleaseChannel.Stable, DateTimeOffset.UtcNow.AddDays(-4)),
            ("1.2.0", ReleaseChannel.Stable, DateTimeOffset.UtcNow.AddDays(-3)),
            ("1.3.0", ReleaseChannel.Stable, DateTimeOffset.UtcNow.AddDays(-2)),
            ("1.4.0", ReleaseChannel.Stable, DateTimeOffset.UtcNow.AddDays(-1)));

        using var client = Factory.CreateClient();
        var page1 = await client.GetFromJsonAsync<JsonElement>("/v2/firmware/versions?limit=2&offset=0");
        await Assert.That(page1.GetProperty("total").GetInt32()).IsEqualTo(5);
        await Assert.That(page1.GetProperty("versions").GetArrayLength()).IsEqualTo(2);

        var page2 = await client.GetFromJsonAsync<JsonElement>("/v2/firmware/versions?limit=2&offset=2");
        await Assert.That(page2.GetProperty("versions").GetArrayLength()).IsEqualTo(2);

        var page3 = await client.GetFromJsonAsync<JsonElement>("/v2/firmware/versions?limit=2&offset=4");
        await Assert.That(page3.GetProperty("versions").GetArrayLength()).IsEqualTo(1);

        var firstPageVersions = page1.GetProperty("versions").EnumerateArray()
            .Select(v => v.GetProperty("version").GetString()).ToList();
        var secondPageVersions = page2.GetProperty("versions").EnumerateArray()
            .Select(v => v.GetProperty("version").GetString()).ToList();
        await Assert.That(firstPageVersions.Intersect(secondPageVersions).Any()).IsFalse();
    }

    [Test]
    public async Task GetVersion_Unknown_Returns404()
    {
        using var client = Factory.CreateClient();
        var response = await client.GetAsync("/v2/firmware/versions/9.9.9");
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task GetVersion_WithReleaseNotes_ReturnsFullDto()
    {
        var boardId = await SeedVersionWithNotesAsync("1.5.1");

        using var client = Factory.CreateClient();
        var body = await client.GetFromJsonAsync<JsonElement>("/v2/firmware/versions/1.5.1");
        await Assert.That(body.GetProperty("version").GetString()).IsEqualTo("1.5.1");
        await Assert.That(body.GetProperty("releaseNotes").GetArrayLength()).IsEqualTo(2);

        var boards = body.GetProperty("boards").EnumerateObject()
            .Select(p => p.Name).ToList();
        await Assert.That(boards).Contains(boardId.ToString());
    }

    [Test]
    public async Task GetVersion_ImmutableCacheHeader()
    {
        await SeedVersionWithNotesAsync("1.5.1");

        using var client = Factory.CreateClient();
        var response = await client.GetAsync("/v2/firmware/versions/1.5.1");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(response.Headers.CacheControl).IsNotNull();
        await Assert.That(response.Headers.CacheControl!.Public).IsTrue();
        await Assert.That(response.Headers.CacheControl.MaxAge).IsEqualTo(TimeSpan.FromSeconds(86400));
    }

    [Test]
    public async Task GetVersionForBoard_UnknownVersion_Returns404()
    {
        using var client = Factory.CreateClient();
        var response = await client.GetAsync($"/v2/firmware/versions/9.9.9/{Guid.NewGuid()}");
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task GetVersionForBoard_HappyPath_ReturnsArtifacts()
    {
        var boardId = await SeedVersionWithNotesAsync("1.5.1");

        using var client = Factory.CreateClient();
        var body = await client.GetFromJsonAsync<JsonElement>(
            $"/v2/firmware/versions/1.5.1/{boardId}");

        await Assert.That(body.GetProperty("version").GetString()).IsEqualTo("1.5.1");
        await Assert.That(body.GetProperty("boardId").GetGuid()).IsEqualTo(boardId);
        await Assert.That(body.GetProperty("artifacts").GetArrayLength()).IsGreaterThanOrEqualTo(1);
    }

    private async Task SeedVersionsAsync(params (string Version, ReleaseChannel Channel, DateTimeOffset ReleaseDate)[] rows)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<RepoServerContext>();

        var repo = new SourceRepository
        {
            Id = Guid.NewGuid(),
            Provider = RepositoryProvider.Github,
            Owner = "openshock",
            Repo = "firmware"
        };
        db.Repositories.Add(repo);

        foreach (var (version, channel, releaseDate) in rows)
        {
            db.FirmwareVersions.Add(new FirmwareVersion
            {
                Version = version,
                Channel = channel,
                ReleaseDate = releaseDate,
                RepositoryId = repo.Id,
                CommitHash = "abc1234567890abcdef1234567890abcdef12345"
            });
        }

        await db.SaveChangesAsync();
    }

    private async Task<Guid> SeedVersionWithNotesAsync(string version)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<RepoServerContext>();

        var chip = new FirmwareChip { Id = Guid.NewGuid(), Name = "ESP32" };
        var board = new FirmwareBoard
        {
            Id = Guid.NewGuid(),
            Name = "Sample-Board",
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
        var fw = new FirmwareVersion
        {
            Version = version,
            Channel = ReleaseChannel.Stable,
            ReleaseDate = DateTimeOffset.UtcNow,
            RepositoryId = repo.Id,
            CommitHash = "abc1234567890abcdef1234567890abcdef12345"
        };

        db.FirmwareChips.Add(chip);
        db.FirmwareBoards.Add(board);
        db.Repositories.Add(repo);
        db.FirmwareVersions.Add(fw);

        db.FirmwareArtifacts.Add(new FirmwareArtifact
        {
            Version = version,
            BoardId = board.Id,
            ArtifactType = FirmwareArtifactType.Merged,
            HashSha256 = new byte[32],
            FileSize = 1024
        });

        db.FirmwareReleaseNotes.Add(new FirmwareReleaseNote
        {
            Version = version,
            Index = 0,
            SectionType = ReleaseNoteSectionType.Breaking,
            Title = "Config format",
            Content = "Changed config format to TOML"
        });
        db.FirmwareReleaseNotes.Add(new FirmwareReleaseNote
        {
            Version = version,
            Index = 1,
            SectionType = ReleaseNoteSectionType.Info,
            Title = null,
            Content = "Fixed WiFi reconnection"
        });

        await db.SaveChangesAsync();
        return board.Id;
    }
}
