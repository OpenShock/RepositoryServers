using Microsoft.EntityFrameworkCore;
using OneOf.Types;
using OpenShock.RepositoryServer.Enums;
using OpenShock.RepositoryServer.RepoServerDb;
using OpenShock.RepositoryServer.RepoServerDb.Models;
using OpenShock.RepositoryServer.Services.Admin;

namespace OpenShock.RepositoryServer.Tests.Integration.Tests;

[NotInParallel("repo-server-integration")]
public class BoardsAdminTests
{
    [ClassDataSource<WebApplicationFactory>(Shared = SharedType.PerTestSession)]
    public required WebApplicationFactory Factory { get; init; }

    [Before(Test)]
    public Task Setup() => Factory.ResetDatabaseAsync();

    private Task<T> Catalog<T>(Func<CatalogAdminService, Task<T>> operation) => Factory.UseAsync(operation);

    private async Task<Guid> SeedChipAsync(string name = "ESP32") =>
        (await Catalog(s => s.CreateChipAsync(name, FirmwareChipArchitecture.Xtensa)))
        .ShouldBe<FirmwareChip>().Id;

    [Test]
    public async Task Create_WithValidChip_StoresBoard()
    {
        var chipId = await SeedChipAsync();

        var result = await Catalog(s => s.CreateBoardAsync(
            "Wemos-D1-Mini-ESP32", chipId, [FirmwareArtifactType.App, FirmwareArtifactType.StaticFs]));

        var board = result.ShouldBe<FirmwareBoard>();
        await Assert.That(board.Name).IsEqualTo("Wemos-D1-Mini-ESP32");
        await Assert.That(board.Discontinued).IsFalse();
        await Assert.That(board.RequiredArtifactTypes.Length).IsEqualTo(2);
    }

    [Test]
    public async Task Create_UnknownChip_ReportsMissingReference()
    {
        var result = await Catalog(s => s.CreateBoardAsync("Some-Board", Guid.NewGuid(), []));

        var missing = result.ShouldBe<ReferenceNotFound>();
        await Assert.That(missing.Reference).IsEqualTo("chip");
    }

    /// <summary>
    /// A board name is interpolated into CDN paths and storage keys unescaped, so anything that could
    /// reshape a key has to be refused. This protection used to come from the request model's data
    /// annotations; with no HTTP layer left, the service is the only thing enforcing it.
    /// </summary>
    [Test]
    [Arguments("OpenShock Core V1")] // spaces
    [Arguments("core/../etc")] // path separator, would escape the board's own prefix
    [Arguments("-leading-dash")] // must start alphanumeric
    [Arguments("emoji-\U0001F600")]
    [Arguments("")]
    public async Task Create_NonUrlSafeName_IsRefused(string name)
    {
        var chipId = await SeedChipAsync();

        var result = await Catalog(s => s.CreateBoardAsync(name, chipId, [FirmwareArtifactType.Merged]));

        result.ShouldBe<InvalidName>();

        var stored = await Factory.UseDbAsync(db => db.FirmwareBoards.CountAsync());
        await Assert.That(stored).IsEqualTo(0);
    }

    [Test]
    [Arguments("Wemos-D1-Mini-ESP32")]
    [Arguments("OpenShock_Core_V2")]
    [Arguments("Waveshare.esp32.s3.zero")]
    [Arguments("NodeMCU32S")]
    public async Task Create_UrlSafeName_IsAccepted(string name)
    {
        var chipId = await SeedChipAsync();

        var result = await Catalog(s => s.CreateBoardAsync(name, chipId, []));

        result.ShouldBe<FirmwareBoard>();
    }

    /// <summary>
    /// Two boards differing only in case would both resolve to whichever the lookup picked, so the
    /// loser could never receive an upload and a hub compiled with its spelling would be served the
    /// other board's firmware.
    /// </summary>
    [Test]
    public async Task Create_NameDifferingOnlyByCase_ReportsConflict()
    {
        var chipId = await SeedChipAsync();
        (await Catalog(s => s.CreateBoardAsync("ESP32-Core", chipId, []))).ShouldBe<FirmwareBoard>();

        var result = await Catalog(s => s.CreateBoardAsync("esp32-core", chipId, []));

        result.ShouldBe<NameConflict>();
    }

    [Test]
    public async Task Create_DeduplicatesRequiredArtifactTypes()
    {
        var chipId = await SeedChipAsync();

        var result = await Catalog(s => s.CreateBoardAsync(
            "Dedup-Board", chipId, [FirmwareArtifactType.App, FirmwareArtifactType.App]));

        var board = result.ShouldBe<FirmwareBoard>();
        await Assert.That(board.RequiredArtifactTypes.Length).IsEqualTo(1);
    }

    [Test]
    public async Task Update_ChangesName()
    {
        var chipId = await SeedChipAsync();
        var board = (await Catalog(s => s.CreateBoardAsync("Old-Name", chipId, []))).ShouldBe<FirmwareBoard>();

        var result = await Catalog(s => s.UpdateBoardAsync(board.Id, "New-Name", chipId, []));
        result.ShouldBe<Success>();

        var stored = await Factory.UseDbAsync(db => db.FirmwareBoards.FirstAsync(b => b.Id == board.Id));
        await Assert.That(stored.Name).IsEqualTo("New-Name");
    }

    [Test]
    public async Task Update_UnknownId_ReportsNotFound()
    {
        var chipId = await SeedChipAsync();

        var result = await Catalog(s => s.UpdateBoardAsync(Guid.NewGuid(), "Any-Name", chipId, []));

        result.ShouldBe<NotFound>();
    }

    [Test]
    public async Task Update_NonUrlSafeName_IsRefused()
    {
        var chipId = await SeedChipAsync();
        var board = (await Catalog(s => s.CreateBoardAsync("Good-Name", chipId, []))).ShouldBe<FirmwareBoard>();

        var result = await Catalog(s => s.UpdateBoardAsync(board.Id, "bad/../name", chipId, []));

        result.ShouldBe<InvalidName>();

        var stored = await Factory.UseDbAsync(db => db.FirmwareBoards.FirstAsync(b => b.Id == board.Id));
        await Assert.That(stored.Name).IsEqualTo("Good-Name");
    }

    [Test]
    public async Task Discontinue_FlipsFlagAndCanBeReversed()
    {
        var chipId = await SeedChipAsync();
        var board = (await Catalog(s => s.CreateBoardAsync("Retired-Board", chipId, []))).ShouldBe<FirmwareBoard>();

        (await Catalog(s => s.SetBoardDiscontinuedAsync(board.Id, true))).ShouldBe<Success>();
        var discontinued = await Factory.UseDbAsync(db => db.FirmwareBoards.FirstAsync(b => b.Id == board.Id));
        await Assert.That(discontinued.Discontinued).IsTrue();

        (await Catalog(s => s.SetBoardDiscontinuedAsync(board.Id, false))).ShouldBe<Success>();
        var revived = await Factory.UseDbAsync(db => db.FirmwareBoards.FirstAsync(b => b.Id == board.Id));
        await Assert.That(revived.Discontinued).IsFalse();
    }

    [Test]
    public async Task Discontinue_UnknownId_ReportsNotFound()
    {
        var result = await Catalog(s => s.SetBoardDiscontinuedAsync(Guid.NewGuid(), true));

        result.ShouldBe<NotFound>();
    }

    [Test]
    public async Task Delete_Unused_Succeeds()
    {
        var chipId = await SeedChipAsync();
        var board = (await Catalog(s => s.CreateBoardAsync("Doomed-Board", chipId, []))).ShouldBe<FirmwareBoard>();

        var result = await Catalog(s => s.DeleteBoardAsync(board.Id));

        result.ShouldBe<Success>();
    }

    [Test]
    public async Task Delete_UnknownId_ReportsNotFound()
    {
        var result = await Catalog(s => s.DeleteBoardAsync(Guid.NewGuid()));

        result.ShouldBe<NotFound>();
    }

    /// <summary>
    /// A board with published artifacts is part of released history; removing it would strand the rows
    /// describing what hubs are currently running.
    /// </summary>
    [Test]
    public async Task Delete_ReferencedByArtifact_ReportsInUse()
    {
        var chipId = await SeedChipAsync();
        var board = (await Catalog(s => s.CreateBoardAsync("Published-Board", chipId, [])))
            .ShouldBe<FirmwareBoard>();

        await Factory.UseDbAsync(async db =>
        {
            var repository = new SourceRepository
            {
                Id = Guid.NewGuid(),
                Provider = RepositoryProvider.Github,
                Owner = "openshock",
                Repo = "firmware"
            };
            db.Repositories.Add(repository);
            db.FirmwareVersions.Add(new FirmwareVersion
            {
                Version = "1.5.1",
                Channel = ReleaseChannel.Stable,
                ReleaseDate = DateTimeOffset.UtcNow,
                RepositoryId = repository.Id,
                CommitHash = "abc1234567890abcdef1234567890abcdef12345"
            });
            db.FirmwareArtifacts.Add(new FirmwareArtifact
            {
                Version = "1.5.1",
                BoardId = board.Id,
                ArtifactType = FirmwareArtifactType.App,
                HashSha256 = new byte[32],
                FileSize = 1024
            });
            await db.SaveChangesAsync();
        });

        var result = await Catalog(s => s.DeleteBoardAsync(board.Id));

        var inUse = result.ShouldBe<InUse>();
        await Assert.That(inUse.ReferencedBy).IsEqualTo("published artifacts");
    }

    [Test]
    public async Task AttachUsbDevice_IsIdempotent()
    {
        var chipId = await SeedChipAsync();
        var board = (await Catalog(s => s.CreateBoardAsync("Usb-Board", chipId, []))).ShouldBe<FirmwareBoard>();
        var device = await Catalog(s => s.UpsertUsbDeviceAsync(0x1A86, 0x7522, "CH9102"));

        (await Catalog(s => s.AttachUsbDeviceToBoardAsync(board.Id, device.Id))).ShouldBe<Success>();
        (await Catalog(s => s.AttachUsbDeviceToBoardAsync(board.Id, device.Id))).ShouldBe<Success>();

        var joins = await Factory.UseDbAsync(db => db.FirmwareBoardUsbDevices.CountAsync());
        await Assert.That(joins).IsEqualTo(1);
    }

    [Test]
    public async Task AttachUsbDevice_UnknownBoard_ReportsMissingReference()
    {
        var device = await Catalog(s => s.UpsertUsbDeviceAsync(0x1A86, 0x7522, "CH9102"));

        var result = await Catalog(s => s.AttachUsbDeviceToBoardAsync(Guid.NewGuid(), device.Id));

        var missing = result.ShouldBe<ReferenceNotFound>();
        await Assert.That(missing.Reference).IsEqualTo("board");
    }

    /// <summary>Detaching a link that is not there is already the desired state.</summary>
    [Test]
    public async Task DetachUsbDevice_IgnoresMissingLink()
    {
        var chipId = await SeedChipAsync();
        var board = (await Catalog(s => s.CreateBoardAsync("Lonely-Board", chipId, []))).ShouldBe<FirmwareBoard>();

        await Catalog(s => s.DetachUsbDeviceFromBoardAsync(board.Id, Guid.NewGuid()));

        var joins = await Factory.UseDbAsync(db => db.FirmwareBoardUsbDevices.CountAsync());
        await Assert.That(joins).IsEqualTo(0);
    }

    [Test]
    public async Task List_IncludesChipAndIsOrderedByName()
    {
        var chipId = await SeedChipAsync();
        (await Catalog(s => s.CreateBoardAsync("Zeta-Board", chipId, []))).ShouldBe<FirmwareBoard>();
        (await Catalog(s => s.CreateBoardAsync("Alpha-Board", chipId, []))).ShouldBe<FirmwareBoard>();

        var boards = await Catalog(s => s.ListBoardsAsync());

        await Assert.That(boards.Select(b => b.Name).ToArray())
            .IsEquivalentTo(new[] { "Alpha-Board", "Zeta-Board" });
        await Assert.That(boards[0].ChipNavigation.Name).IsEqualTo("ESP32");
    }
}
