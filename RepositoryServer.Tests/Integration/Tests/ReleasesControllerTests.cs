using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using OpenShock.RepositoryServer.Enums;
using OpenShock.RepositoryServer.Models.Firmware;
using OpenShock.RepositoryServer.RepoServerDb;

namespace OpenShock.RepositoryServer.Tests.Integration.Tests;

/// <summary>
/// Covers the CI/CD ingestion state machine: init, artifact upload, publish, abort — and in
/// particular the authorization boundary between two registered repositories.
/// </summary>
[NotInParallel("repo-server-integration")]
public class ReleasesControllerTests
{
    private const string BoardName = "Wemos-D1-Mini-ESP32";
    private const string Changelog = "### Info\n- Something changed";

    [ClassDataSource<WebApplicationFactory>(Shared = SharedType.PerTestSession)]
    public required WebApplicationFactory Factory { get; init; }

    [Before(Test)]
    public Task Setup() => Factory.ResetDatabaseAsync();

    // ---- Happy path ----

    [Test]
    public async Task FullReleaseLifecycle_InitUploadPublish_MakesVersionPublic()
    {
        var seed = await SeedAsync();
        using var client = Factory.CreateCiCdClient(seed.RepositoryId);

        var releaseId = await InitReleaseAsync(client, "1.5.1");

        var upload = await UploadArtifactsAsync(client, releaseId, BoardName);
        await Assert.That(upload.StatusCode).IsEqualTo(HttpStatusCode.OK);

        var publish = await client.PostAsync($"/2/firmware/releases/{releaseId}/publish", null);
        await Assert.That(publish.IsSuccessStatusCode).IsTrue();

        // Once published it must be visible on the public read path.
        using var anon = Factory.CreateClient();
        var latest = await anon.GetFromJsonAsync<JsonElement>("/2/firmware/latest/stable");
        await Assert.That(latest.GetProperty("version").GetString()).IsEqualTo("1.5.1");
    }

    // ---- Ownership (B2) ----

    [Test]
    public async Task Upload_FromDifferentRepository_IsForbidden()
    {
        var seed = await SeedAsync();
        var intruderId = await RegisterRepositoryAsync("attacker", "evil-repo");

        using var owner = Factory.CreateCiCdClient(seed.RepositoryId);
        var releaseId = await InitReleaseAsync(owner, "1.5.1");

        // A second registered repository is fully authenticated, but must not be able to inject
        // binaries into someone else's in-flight release.
        using var intruder = Factory.CreateCiCdClient(intruderId);
        var response = await UploadArtifactsAsync(intruder, releaseId, BoardName);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task Publish_FromDifferentRepository_IsForbidden()
    {
        var seed = await SeedAsync();
        var intruderId = await RegisterRepositoryAsync("attacker", "evil-repo");

        using var owner = Factory.CreateCiCdClient(seed.RepositoryId);
        var releaseId = await InitReleaseAsync(owner, "1.5.1");
        await UploadArtifactsAsync(owner, releaseId, BoardName);

        // Publishing someone else's release would attribute it to their repository and commit hash.
        using var intruder = Factory.CreateCiCdClient(intruderId);
        var response = await intruder.PostAsync($"/2/firmware/releases/{releaseId}/publish", null);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task Abort_FromDifferentRepository_IsForbidden()
    {
        var seed = await SeedAsync();
        var intruderId = await RegisterRepositoryAsync("attacker", "evil-repo");

        using var owner = Factory.CreateCiCdClient(seed.RepositoryId);
        var releaseId = await InitReleaseAsync(owner, "1.5.1");

        using var intruder = Factory.CreateCiCdClient(intruderId);
        var response = await intruder.DeleteAsync($"/2/firmware/releases/{releaseId}");
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task Releases_WithoutCiCdPrincipal_AreRejected()
    {
        await SeedAsync();

        using var anon = Factory.CreateClient();
        var response = await anon.PostAsJsonAsync("/2/firmware/releases", new InitReleaseRequest
        {
            Version = "1.5.1",
            Channel = "stable",
            ReleaseDate = DateTimeOffset.UtcNow,
            Boards = [BoardName],
            Changelog = Changelog
        });

        await Assert.That(response.IsSuccessStatusCode).IsFalse();
    }

    [Test]
    public async Task Releases_WithOnlyTheModulesScope_AreForbidden()
    {
        var seed = await SeedAsync();

        // Firmware and desktop ingestion share the CI/CD scheme, so a repository onboarded purely to
        // publish desktop modules must not be able to start a firmware release.
        using var client = Factory.CreateCiCdClient(seed.RepositoryId, scopes: "publish_modules");
        var response = await client.PostAsJsonAsync("/2/firmware/releases", new InitReleaseRequest
        {
            Version = "1.5.1",
            Channel = "stable",
            ReleaseDate = DateTimeOffset.UtcNow,
            Boards = [BoardName],
            Changelog = Changelog
        });

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task Releases_WithNoScopes_AreForbidden()
    {
        var seed = await SeedAsync();

        using var client = Factory.CreateCiCdClient(seed.RepositoryId, scopes: TestCiCdAuthHandler.NoScopes);
        var response = await client.PostAsJsonAsync("/2/firmware/releases", new InitReleaseRequest
        {
            Version = "1.5.1",
            Channel = "stable",
            ReleaseDate = DateTimeOffset.UtcNow,
            Boards = [BoardName],
            Changelog = Changelog
        });

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
    }

    // ---- Immutability (B3) ----

    [Test]
    public async Task Init_ForAlreadyPublishedVersion_IsRejected()
    {
        var seed = await SeedAsync();
        using var client = Factory.CreateCiCdClient(seed.RepositoryId);

        var releaseId = await InitReleaseAsync(client, "1.5.1");
        await UploadArtifactsAsync(client, releaseId, BoardName);
        await client.PostAsync($"/2/firmware/releases/{releaseId}/publish", null);

        // Re-initialising a published version would let a second release overwrite live artifacts at
        // the same storage keys, and if abandoned, have them deleted by the TTL job.
        var response = await client.PostAsJsonAsync("/2/firmware/releases", new InitReleaseRequest
        {
            Version = "1.5.1",
            Channel = "stable",
            ReleaseDate = DateTimeOffset.UtcNow,
            Boards = [BoardName],
            Changelog = Changelog
        });

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
    }

    [Test]
    public async Task Init_WhileAnotherReleaseIsStaging_IsRejected()
    {
        var seed = await SeedAsync();
        using var client = Factory.CreateCiCdClient(seed.RepositoryId);

        await InitReleaseAsync(client, "1.5.1");

        var response = await client.PostAsJsonAsync("/2/firmware/releases", new InitReleaseRequest
        {
            Version = "1.5.1",
            Channel = "stable",
            ReleaseDate = DateTimeOffset.UtcNow,
            Boards = [BoardName],
            Changelog = Changelog
        });

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
    }

    // ---- Validation ----

    [Test]
    public async Task Init_UnknownBoard_NamesTheOffendingBoard()
    {
        var seed = await SeedAsync();
        using var client = Factory.CreateCiCdClient(seed.RepositoryId);

        var response = await client.PostAsJsonAsync("/2/firmware/releases", new InitReleaseRequest
        {
            Version = "1.5.1",
            Channel = "stable",
            ReleaseDate = DateTimeOffset.UtcNow,
            Boards = [BoardName, "No-Such-Board"],
            Changelog = Changelog
        });

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);

        // A CI operator reading this in a log needs the board name, not a UUID.
        var body = await response.Content.ReadAsStringAsync();
        await Assert.That(body).Contains("No-Such-Board");
    }

    [Test]
    public async Task Init_InvalidChangelogWithoutNofail_Fails()
    {
        var seed = await SeedAsync();
        using var client = Factory.CreateCiCdClient(seed.RepositoryId);

        var response = await client.PostAsJsonAsync("/2/firmware/releases", new InitReleaseRequest
        {
            Version = "1.5.1",
            Channel = "stable",
            ReleaseDate = DateTimeOffset.UtcNow,
            Boards = [BoardName],
            Changelog = "no headings here at all"
        });

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task Init_InvalidChangelogWithNofail_LandsInEditing()
    {
        var seed = await SeedAsync();
        using var client = Factory.CreateCiCdClient(seed.RepositoryId);

        var response = await client.PostAsJsonAsync("/2/firmware/releases?nofail", new InitReleaseRequest
        {
            Version = "1.5.1",
            Channel = "stable",
            ReleaseDate = DateTimeOffset.UtcNow,
            Boards = [BoardName],
            Changelog = "no headings here at all"
        });

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        await Assert.That(body.GetProperty("status").GetString()).IsEqualTo("editing");
    }

    [Test]
    public async Task Publish_WhileEditing_IsRejected()
    {
        var seed = await SeedAsync();
        using var client = Factory.CreateCiCdClient(seed.RepositoryId);

        var init = await client.PostAsJsonAsync("/2/firmware/releases?nofail", new InitReleaseRequest
        {
            Version = "1.5.1",
            Channel = "stable",
            ReleaseDate = DateTimeOffset.UtcNow,
            Boards = [BoardName],
            Changelog = "no headings here at all"
        });
        var releaseId = (await init.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        await UploadArtifactsAsync(client, releaseId, BoardName);

        var publish = await client.PostAsync($"/2/firmware/releases/{releaseId}/publish", null);
        await Assert.That(publish.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
    }

    [Test]
    public async Task Publish_WithoutAllDeclaredBoards_NamesTheMissingBoard()
    {
        var seed = await SeedAsync(extraBoardName: "Wemos-Lolin-S3");
        using var client = Factory.CreateCiCdClient(seed.RepositoryId);

        var init = await client.PostAsJsonAsync("/2/firmware/releases", new InitReleaseRequest
        {
            Version = "1.5.1",
            Channel = "stable",
            ReleaseDate = DateTimeOffset.UtcNow,
            Boards = [BoardName, "Wemos-Lolin-S3"],
            Changelog = Changelog
        });
        var releaseId = (await init.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        await UploadArtifactsAsync(client, releaseId, BoardName);

        var publish = await client.PostAsync($"/2/firmware/releases/{releaseId}/publish", null);
        await Assert.That(publish.IsSuccessStatusCode).IsFalse();

        var body = await publish.Content.ReadAsStringAsync();
        await Assert.That(body).Contains("Wemos-Lolin-S3");
    }

    [Test]
    public async Task Upload_WithMismatchedSha256_IsRejected()
    {
        var seed = await SeedAsync();
        using var client = Factory.CreateCiCdClient(seed.RepositoryId);
        var releaseId = await InitReleaseAsync(client, "1.5.1");

        var content = new MultipartFormDataContent();
        var bytes = Encoding.UTF8.GetBytes("merged-artifact-bytes");
        content.Add(new ByteArrayContent(bytes), "merged", "firmware.bin");
        // A hash the payload does not have.
        content.Add(new StringContent(
            JsonSerializer.Serialize(new Dictionary<string, string> { ["merged"] = new string('a', 64) })),
            "sha256");

        var response = await client.PutAsync(
            $"/2/firmware/releases/{releaseId}/boards/{BoardName}", content);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task Upload_ForUndeclaredBoard_IsRejected()
    {
        var seed = await SeedAsync(extraBoardName: "Wemos-Lolin-S3");
        using var client = Factory.CreateCiCdClient(seed.RepositoryId);
        var releaseId = await InitReleaseAsync(client, "1.5.1");

        var response = await UploadArtifactsAsync(client, releaseId, "Wemos-Lolin-S3");
        await Assert.That(response.IsSuccessStatusCode).IsFalse();
    }

    [Test]
    public async Task Abort_ByOwner_MakesReleaseUnpublishable()
    {
        var seed = await SeedAsync();
        using var client = Factory.CreateCiCdClient(seed.RepositoryId);
        var releaseId = await InitReleaseAsync(client, "1.5.1");

        var abort = await client.DeleteAsync($"/2/firmware/releases/{releaseId}");
        await Assert.That(abort.StatusCode).IsEqualTo(HttpStatusCode.NoContent);

        var publish = await client.PostAsync($"/2/firmware/releases/{releaseId}/publish", null);
        await Assert.That(publish.IsSuccessStatusCode).IsFalse();
    }

    // ---- Atomicity and coercion ----

    [Test]
    public async Task Upload_PartialFailure_LeavesNoOrphanedBlobsOrLostState()
    {
        var seed = await SeedAsync();
        using var client = Factory.CreateCiCdClient(seed.RepositoryId);
        var releaseId = await InitReleaseAsync(client, "1.5.1");

        // A valid first upload establishes staged state we must not lose.
        await UploadArtifactsAsync(client, releaseId, BoardName);

        // Now send two files where only one hashes correctly.
        var goodBytes = Encoding.UTF8.GetBytes("good-app-bytes");
        var badBytes = Encoding.UTF8.GetBytes("bad-merged-bytes");
        var content = new MultipartFormDataContent();
        content.Add(new ByteArrayContent(goodBytes), "app", "app.bin");
        content.Add(new ByteArrayContent(badBytes), "merged", "firmware.bin");
        content.Add(new StringContent(JsonSerializer.Serialize(new Dictionary<string, string>
        {
            ["app"] = Convert.ToHexString(SHA256.HashData(goodBytes)),
            ["merged"] = new string('a', 64)
        })), "sha256");

        var response = await client.PutAsync(
            $"/2/firmware/releases/{releaseId}/boards/{BoardName}", content);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);

        // The prior staged artifact must survive: nothing is deleted or written until every file in
        // the request has been verified.
        await using var scope = Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<RepoServerContext>();
        var staged = db.FirmwareStagedArtifacts.Where(a => a.ReleaseId == releaseId).ToList();
        await Assert.That(staged).Count().IsEqualTo(1);
        await Assert.That(staged[0].ArtifactType).IsEqualTo(FirmwareArtifactType.Merged);
    }

    [Test]
    public async Task Init_NonUtcReleaseDate_IsAcceptedAndNormalized()
    {
        var seed = await SeedAsync();
        using var client = Factory.CreateCiCdClient(seed.RepositoryId);

        // Npgsql rejects a non-zero offset for `timestamp with time zone`, which used to surface as a
        // generic 500 for any CI runner outside UTC.
        var response = await client.PostAsJsonAsync("/2/firmware/releases", new InitReleaseRequest
        {
            Version = "1.5.1",
            Channel = "stable",
            ReleaseDate = new DateTimeOffset(2026, 4, 15, 12, 0, 0, TimeSpan.FromHours(2)),
            Boards = [BoardName],
            Changelog = Changelog
        });

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Created);
    }

    // ---- Staging isolation ----

    [Test]
    public async Task Upload_DoesNotWriteThePublishedKey()
    {
        var seed = await SeedAsync();
        using var client = Factory.CreateCiCdClient(seed.RepositoryId);
        var releaseId = await InitReleaseAsync(client, "1.5.1");

        await UploadArtifactsAsync(client, releaseId, BoardName);

        // Nothing is readable at its published path until publish runs.
        await Assert.That(Factory.StoredFileExists($"1.5.1/{seed.BoardId}/firmware.bin")).IsFalse();
        await Assert.That(Factory.StoredFileExists($"_staging/{releaseId}/{seed.BoardId}/firmware.bin")).IsTrue();
    }

    [Test]
    public async Task Publish_PromotesStagedArtifactsToPublishedKeys()
    {
        var seed = await SeedAsync();
        using var client = Factory.CreateCiCdClient(seed.RepositoryId);
        var releaseId = await InitReleaseAsync(client, "1.5.1");
        await UploadArtifactsAsync(client, releaseId, BoardName);

        var publish = await client.PostAsync($"/2/firmware/releases/{releaseId}/publish", null);
        await Assert.That(publish.IsSuccessStatusCode).IsTrue();

        await Assert.That(Factory.StoredFileExists($"1.5.1/{seed.BoardId}/firmware.bin")).IsTrue();

        // And the staging copies are cleared once they are dead weight.
        await Assert.That(Factory.StoredFileExists($"_staging/{releaseId}/{seed.BoardId}/firmware.bin")).IsFalse();
    }

    [Test]
    public async Task Abort_RemovesStagedArtifactsButNeverPublishedOnes()
    {
        var seed = await SeedAsync();
        using var client = Factory.CreateCiCdClient(seed.RepositoryId);

        // Publish 1.5.1 so there are live artifacts on disk to protect.
        var publishedId = await InitReleaseAsync(client, "1.5.1");
        await UploadArtifactsAsync(client, publishedId, BoardName);
        await client.PostAsync($"/2/firmware/releases/{publishedId}/publish", null);

        // A second, unrelated release is staged and then aborted.
        var abortedId = await InitReleaseAsync(client, "1.6.0");
        await UploadArtifactsAsync(client, abortedId, BoardName);
        var abort = await client.DeleteAsync($"/2/firmware/releases/{abortedId}");
        await Assert.That(abort.StatusCode).IsEqualTo(HttpStatusCode.NoContent);

        await Assert.That(Factory.StoredFileExists($"_staging/{abortedId}/{seed.BoardId}/firmware.bin")).IsFalse();
        await Assert.That(Factory.StoredFileExists($"1.5.1/{seed.BoardId}/firmware.bin")).IsTrue();
    }

    // ---- Helpers ----

    private sealed record Seed(Guid RepositoryId, Guid BoardId, Guid ChipId);

    private async Task<Seed> SeedAsync(string? extraBoardName = null)
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
        var chip = new FirmwareChip
        {
            Id = Guid.NewGuid(),
            Name = "ESP32",
            Architecture = FirmwareChipArchitecture.Xtensa
        };
        var board = new FirmwareBoard
        {
            Id = Guid.NewGuid(),
            Name = BoardName,
            ChipId = chip.Id,
            RequiredArtifactTypes = []
        };

        db.Repositories.Add(repo);
        db.FirmwareChips.Add(chip);
        db.FirmwareBoards.Add(board);

        if (extraBoardName is not null)
        {
            db.FirmwareBoards.Add(new FirmwareBoard
            {
                Id = Guid.NewGuid(),
                Name = extraBoardName,
                ChipId = chip.Id,
                RequiredArtifactTypes = []
            });
        }

        await db.SaveChangesAsync();
        return new Seed(repo.Id, board.Id, chip.Id);
    }

    private async Task<Guid> RegisterRepositoryAsync(string owner, string repo)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<RepoServerContext>();

        var row = new SourceRepository
        {
            Id = Guid.NewGuid(),
            Provider = RepositoryProvider.Github,
            Owner = owner,
            Repo = repo
        };
        db.Repositories.Add(row);
        await db.SaveChangesAsync();
        return row.Id;
    }

    private static async Task<Guid> InitReleaseAsync(HttpClient client, string version)
    {
        var response = await client.PostAsJsonAsync("/2/firmware/releases", new InitReleaseRequest
        {
            Version = version,
            Channel = "stable",
            ReleaseDate = DateTimeOffset.UtcNow,
            Boards = [BoardName],
            Changelog = Changelog
        });

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("id").GetGuid();
    }

    private static async Task<HttpResponseMessage> UploadArtifactsAsync(
        HttpClient client, Guid releaseId, string board)
    {
        var bytes = Encoding.UTF8.GetBytes($"merged-artifact-for-{board}");
        var hash = Convert.ToHexString(SHA256.HashData(bytes));

        var content = new MultipartFormDataContent();
        content.Add(new ByteArrayContent(bytes), "merged", "firmware.bin");
        content.Add(new StringContent(
            JsonSerializer.Serialize(new Dictionary<string, string> { ["merged"] = hash })),
            "sha256");

        return await client.PutAsync($"/2/firmware/releases/{releaseId}/boards/{board}", content);
    }
}
