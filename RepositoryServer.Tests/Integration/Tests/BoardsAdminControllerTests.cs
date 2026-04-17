using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using OpenShock.RepositoryServer.Enums;
using OpenShock.RepositoryServer.Models.Firmware;
using OpenShock.RepositoryServer.RepoServerDb;

namespace OpenShock.RepositoryServer.Tests.Integration.Tests;

[NotInParallel("repo-server-integration")]
public class BoardsAdminControllerTests
{
    private const string BasePath = "/v2/firmware/admin/boards";
    private const string ChipsPath = "/v2/firmware/admin/chips";

    [ClassDataSource<WebApplicationFactory>(Shared = SharedType.PerTestSession)]
    public required WebApplicationFactory Factory { get; init; }

    [Before(Test)]
    public Task Setup() => Factory.ResetDatabaseAsync();

    [Test]
    public async Task Post_WithValidChip_Returns201()
    {
        var chipId = await SeedChipAsync("ESP32-S3");
        using var client = Factory.CreateAdminClient();

        var response = await client.PostAsJsonAsync(BasePath, new CreateFirmwareBoardRequest
        {
            Name = "OpenShock Core V1",
            ChipId = chipId,
            RequiredArtifactTypes = ["merged"]
        });

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        await Assert.That(body.GetProperty("id").GetGuid()).IsNotEqualTo(Guid.Empty);
    }

    [Test]
    public async Task Post_UnknownChip_Returns404()
    {
        using var client = Factory.CreateAdminClient();
        var response = await client.PostAsJsonAsync(BasePath, new CreateFirmwareBoardRequest
        {
            Name = "Phantom Board",
            ChipId = Guid.NewGuid(),
            RequiredArtifactTypes = ["merged"]
        });
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task Post_InvalidArtifactType_Returns400()
    {
        var chipId = await SeedChipAsync("ESP32");
        using var client = Factory.CreateAdminClient();
        var response = await client.PostAsJsonAsync(BasePath, new CreateFirmwareBoardRequest
        {
            Name = "bad-board",
            ChipId = chipId,
            RequiredArtifactTypes = ["unknown_type"]
        });
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task Patch_Discontinue_FlipsFlag()
    {
        var chipId = await SeedChipAsync("ESP32");
        var boardId = await SeedBoardAsync(chipId, "Pishock-Lite-2021");

        using var client = Factory.CreateAdminClient();
        var response = await client.PatchAsync($"{BasePath}/{boardId}/discontinue", content: null);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        await using var scope = Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<RepoServerContext>();
        var board = await db.FirmwareBoards.FindAsync(boardId);
        await Assert.That(board!.Discontinued).IsTrue();
    }

    [Test]
    public async Task Delete_Unused_Returns204()
    {
        var chipId = await SeedChipAsync("ESP32-C3");
        var boardId = await SeedBoardAsync(chipId, "Generic-C3");

        using var client = Factory.CreateAdminClient();
        var response = await client.DeleteAsync($"{BasePath}/{boardId}");
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
    }

    [Test]
    public async Task Delete_ReferencedByArtifact_Returns409InUse()
    {
        var chipId = await SeedChipAsync("ESP32");
        var boardId = await SeedBoardAsync(chipId, "Wemos-D1-Mini-ESP32");

        await using (var scope = Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RepoServerContext>();

            // Set up: a repository, a firmware version, and an artifact referencing the board.
            var repo = new SourceRepository
            {
                Id = Guid.NewGuid(),
                Provider = RepositoryProvider.Github,
                Owner = "openshock",
                Repo = "firmware"
            };
            var version = new FirmwareVersion
            {
                Version = "1.5.1",
                Channel = ReleaseChannel.Stable,
                ReleaseDate = DateTimeOffset.UtcNow,
                RepositoryId = repo.Id,
                CommitHash = "abc1234567890abcdef1234567890abcdef12345"
            };
            db.Repositories.Add(repo);
            db.FirmwareVersions.Add(version);
            db.FirmwareArtifacts.Add(new FirmwareArtifact
            {
                Version = version.Version,
                BoardId = boardId,
                ArtifactType = FirmwareArtifactType.Merged,
                HashSha256 = new byte[32],
                FileSize = 1024
            });
            await db.SaveChangesAsync();
        }

        using var client = Factory.CreateAdminClient();
        var response = await client.DeleteAsync($"{BasePath}/{boardId}");
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
    }

    [Test]
    public async Task Put_UpdateBoard_ChangesName()
    {
        var chipId = await SeedChipAsync("ESP32");
        var boardId = await SeedBoardAsync(chipId, "Original-Name");

        using var client = Factory.CreateAdminClient();
        var response = await client.PutAsJsonAsync($"{BasePath}/{boardId}", new CreateFirmwareBoardRequest
        {
            Name = "New-Name",
            ChipId = chipId,
            RequiredArtifactTypes = ["merged", "app"]
        });
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        await using var scope = Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<RepoServerContext>();
        var board = await db.FirmwareBoards.FindAsync(boardId);
        await Assert.That(board!.Name).IsEqualTo("New-Name");
        await Assert.That(board.RequiredArtifactTypes.Length).IsEqualTo(2);
    }

    [Test]
    public async Task AttachUsbDevice_Idempotent_WritesSingleRow()
    {
        var chipId = await SeedChipAsync("ESP32");
        var boardId = await SeedBoardAsync(chipId, "Sample-Board");

        Guid deviceId;
        await using (var scope = Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RepoServerContext>();
            var device = new UsbDevice { Id = Guid.NewGuid(), Vid = 0x1A86, Pid = 0x7522, Name = "CH9102" };
            db.UsbDevices.Add(device);
            await db.SaveChangesAsync();
            deviceId = device.Id;
        }

        using var client = Factory.CreateAdminClient();
        var first = await client.PutAsync($"{BasePath}/{boardId}/usb-devices/{deviceId}", content: null);
        var second = await client.PutAsync($"{BasePath}/{boardId}/usb-devices/{deviceId}", content: null);

        await Assert.That(first.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
        await Assert.That(second.StatusCode).IsEqualTo(HttpStatusCode.NoContent);

        await using var verify = Factory.Services.CreateAsyncScope();
        var db2 = verify.ServiceProvider.GetRequiredService<RepoServerContext>();
        var count = db2.FirmwareBoardUsbDevices.Count(j => j.BoardId == boardId && j.UsbDeviceId == deviceId);
        await Assert.That(count).IsEqualTo(1);
    }

    [Test]
    public async Task DetachUsbDevice_IgnoresMissingLink()
    {
        using var client = Factory.CreateAdminClient();
        var response = await client.DeleteAsync(
            $"{BasePath}/{Guid.NewGuid()}/usb-devices/{Guid.NewGuid()}");
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
    }

    private async Task<Guid> SeedChipAsync(string name)
    {
        using var client = Factory.CreateAdminClient();
        var response = await client.PostAsJsonAsync(ChipsPath, new CreateFirmwareChipRequest
        {
            Name = name
        });
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("id").GetGuid();
    }

    private async Task<Guid> SeedBoardAsync(Guid chipId, string name)
    {
        using var client = Factory.CreateAdminClient();
        var response = await client.PostAsJsonAsync(BasePath, new CreateFirmwareBoardRequest
        {
            Name = name,
            ChipId = chipId,
            RequiredArtifactTypes = []
        });
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("id").GetGuid();
    }
}
