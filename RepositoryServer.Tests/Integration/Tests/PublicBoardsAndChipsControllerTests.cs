using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using OpenShock.RepositoryServer.Enums;
using OpenShock.RepositoryServer.Models.Firmware;
using OpenShock.RepositoryServer.RepoServerDb;

namespace OpenShock.RepositoryServer.Tests.Integration.Tests;

[NotInParallel("repo-server-integration")]
public class PublicBoardsAndChipsControllerTests
{
    [ClassDataSource<WebApplicationFactory>(Shared = SharedType.PerTestSession)]
    public required WebApplicationFactory Factory { get; init; }

    [Before(Test)]
    public Task Setup() => Factory.ResetDatabaseAsync();

    [Test]
    public async Task ListBoards_FilterByChipId_ReturnsOnlyMatching()
    {
        Guid chipAId;
        Guid chipBId;
        await using (var scope = Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RepoServerContext>();
            var chipA = new FirmwareChip { Id = Guid.NewGuid(), Name = "ESP32-S3" };
            var chipB = new FirmwareChip { Id = Guid.NewGuid(), Name = "ESP32-C3" };
            db.FirmwareChips.Add(chipA);
            db.FirmwareChips.Add(chipB);
            db.FirmwareBoards.Add(new FirmwareBoard
            {
                Id = Guid.NewGuid(), Name = "Board-S3", ChipId = chipA.Id, RequiredArtifactTypes = []
            });
            db.FirmwareBoards.Add(new FirmwareBoard
            {
                Id = Guid.NewGuid(), Name = "Board-C3", ChipId = chipB.Id, RequiredArtifactTypes = []
            });
            await db.SaveChangesAsync();
            chipAId = chipA.Id;
            chipBId = chipB.Id;
        }

        using var client = Factory.CreateClient();
        var boardsForChipA = await client.GetFromJsonAsync<List<FirmwareBoardDto>>(
            $"/v2/firmware/boards?chipId={chipAId}");
        await Assert.That(boardsForChipA).IsNotNull();
        await Assert.That(boardsForChipA!).HasCount(1);
        await Assert.That(boardsForChipA[0].ChipId).IsEqualTo(chipAId);
    }

    [Test]
    public async Task ListBoards_ExcludeDiscontinued_HidesEOLBoards()
    {
        await using (var scope = Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RepoServerContext>();
            var chip = new FirmwareChip { Id = Guid.NewGuid(), Name = "ESP32" };
            db.FirmwareChips.Add(chip);
            db.FirmwareBoards.Add(new FirmwareBoard
            {
                Id = Guid.NewGuid(), Name = "Active", ChipId = chip.Id,
                Discontinued = false, RequiredArtifactTypes = []
            });
            db.FirmwareBoards.Add(new FirmwareBoard
            {
                Id = Guid.NewGuid(), Name = "EOL", ChipId = chip.Id,
                Discontinued = true, RequiredArtifactTypes = []
            });
            await db.SaveChangesAsync();
        }

        using var client = Factory.CreateClient();
        var withDiscontinued = await client.GetFromJsonAsync<List<FirmwareBoardDto>>(
            "/v2/firmware/boards?includeDiscontinued=true");
        var withoutDiscontinued = await client.GetFromJsonAsync<List<FirmwareBoardDto>>(
            "/v2/firmware/boards?includeDiscontinued=false");

        await Assert.That(withDiscontinued!).HasCount(2);
        await Assert.That(withoutDiscontinued!).HasCount(1);
        await Assert.That(withoutDiscontinued![0].Name).IsEqualTo("Active");
    }

    [Test]
    public async Task ListChips_ReturnsArchitectureLowercase()
    {
        await using (var scope = Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RepoServerContext>();
            db.FirmwareChips.Add(new FirmwareChip
            {
                Id = Guid.NewGuid(),
                Name = "ESP32-C6",
                Architecture = FirmwareChipArchitecture.RiscV
            });
            await db.SaveChangesAsync();
        }

        using var client = Factory.CreateClient();
        var chips = await client.GetFromJsonAsync<List<FirmwareChipDto>>("/v2/firmware/chips");
        await Assert.That(chips).IsNotNull();
        await Assert.That(chips!).HasCount(1);
        await Assert.That(chips[0].Name).IsEqualTo("ESP32-C6");
        await Assert.That(chips[0].Architecture).IsEqualTo("risc_v");
    }
}
