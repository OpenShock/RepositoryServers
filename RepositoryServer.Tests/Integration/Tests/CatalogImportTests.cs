using Microsoft.EntityFrameworkCore;
using OpenShock.RepositoryServer.Enums;
using OpenShock.RepositoryServer.Models.Admin;
using OpenShock.RepositoryServer.RepoServerDb;
using OpenShock.RepositoryServer.Services.Admin;

namespace OpenShock.RepositoryServer.Tests.Integration.Tests;

[NotInParallel("repo-server-integration")]
public class CatalogImportTests
{
    [ClassDataSource<WebApplicationFactory>(Shared = SharedType.PerTestSession)]
    public required WebApplicationFactory Factory { get; init; }

    [Before(Test)]
    public Task Setup() => Factory.ResetDatabaseAsync();

    private Task<T> Import<T>(Func<CatalogImportService, Task<T>> operation) => Factory.UseAsync(operation);

    private static CatalogImportDocument MinimalCatalog() => new()
    {
        Chips = [new ChipImport("ESP32-S3", "xtensa")],
        Boards = [new BoardImport("OpenShock-Core-V2", "ESP32-S3")]
    };

    /// <summary>
    /// The case the import exists for: nothing in the database, one file, everything resolvable.
    /// </summary>
    [Test]
    public async Task Apply_SeedsFreshInstance()
    {
        var result = await Import(s => s.ApplyAsync(MinimalCatalog()));

        await Assert.That(result.Blocked).IsEqualTo(0);

        await Factory.UseAsync(async (RepoServerContext db) =>
        {
            var board = await db.FirmwareBoards.Include(b => b.ChipNavigation)
                .FirstOrDefaultAsync(b => b.Name == "OpenShock-Core-V2");

            await Assert.That(board).IsNotNull();
            await Assert.That(board!.ChipNavigation.Name).IsEqualTo("ESP32-S3");
            // The default when a board does not spell out requiredArtifacts: what an OTA needs.
            await Assert.That(board.RequiredArtifactTypes).Contains(FirmwareArtifactType.App);
            await Assert.That(board.RequiredArtifactTypes).Contains(FirmwareArtifactType.StaticFs);
            return true;
        });
    }

    /// <summary>
    /// A board may name a chip that only exists in the same document. Without this a fresh-instance
    /// import would be blocked on every board, which is the one case it has to handle.
    /// </summary>
    [Test]
    public async Task Plan_BoardResolvesChipDeclaredInSameDocument()
    {
        var plan = await Import(s => s.PlanAsync(MinimalCatalog()));

        await Assert.That(plan.Blocked).IsEqualTo(0);
        await Assert.That(plan.Creates).IsEqualTo(2);
    }

    /// <summary>
    /// Re-running the same file must not create second rows or report phantom changes, since that is
    /// what makes the file safe to keep in git and re-apply.
    /// </summary>
    [Test]
    public async Task Apply_IsIdempotent()
    {
        await Import(s => s.ApplyAsync(MinimalCatalog()));

        var plan = await Import(s => s.PlanAsync(MinimalCatalog()));

        await Assert.That(plan.Creates).IsEqualTo(0);
        await Assert.That(plan.Updates).IsEqualTo(0);
        await Assert.That(plan.Unchanged).IsEqualTo(2);
        await Assert.That(plan.CanApply).IsFalse();

        await Factory.UseAsync(async (RepoServerContext db) =>
        {
            await Assert.That(await db.FirmwareChips.CountAsync()).IsEqualTo(1);
            await Assert.That(await db.FirmwareBoards.CountAsync()).IsEqualTo(1);
            return true;
        });
    }

    /// <summary>
    /// A board naming a chip that is neither in the file nor on the server blocks the whole import.
    /// Applying the resolvable half would leave a catalog that looks seeded and fails later, at a
    /// publish, on the board nobody noticed was missing.
    /// </summary>
    [Test]
    public async Task Apply_UnknownChipReference_WritesNothing()
    {
        var document = new CatalogImportDocument
        {
            Chips = [new ChipImport("ESP32-S3", "xtensa")],
            Boards =
            [
                new BoardImport("OpenShock-Core-V2", "ESP32-S3"),
                new BoardImport("Seeed-Xiao-ESP32C3", "ESP32-C3")
            ]
        };

        var result = await Import(s => s.ApplyAsync(document));

        await Assert.That(result.Blocked).IsEqualTo(1);

        await Factory.UseAsync(async (RepoServerContext db) =>
        {
            await Assert.That(await db.FirmwareChips.CountAsync()).IsEqualTo(0);
            await Assert.That(await db.FirmwareBoards.CountAsync()).IsEqualTo(0);
            return true;
        });
    }

    /// <summary>
    /// Two rows under one key would apply in file order and leave the last silently winning.
    /// </summary>
    [Test]
    public async Task Plan_DuplicateBoardName_IsBlocked()
    {
        var document = new CatalogImportDocument
        {
            Chips = [new ChipImport("ESP32", "xtensa")],
            Boards =
            [
                new BoardImport("NodeMCU-32S", "ESP32"),
                new BoardImport("nodemcu-32s", "ESP32")
            ]
        };

        var plan = await Import(s => s.PlanAsync(document));

        await Assert.That(plan.Blocked).IsGreaterThan(0);
        await Assert.That(plan.CanApply).IsFalse();
    }

    /// <summary>
    /// Board names reach storage keys unescaped, so the import is held to the same character rules
    /// as the Boards page rather than trusting the file.
    /// </summary>
    [Test]
    public async Task Plan_InvalidBoardName_IsBlocked()
    {
        var document = new CatalogImportDocument
        {
            Chips = [new ChipImport("ESP32", "xtensa")],
            Boards = [new BoardImport("../escape", "ESP32")]
        };

        var plan = await Import(s => s.PlanAsync(document));

        await Assert.That(plan.Blocked).IsEqualTo(1);
    }

    /// <summary>
    /// Scopes are replaced rather than merged, so the file is the whole grant. This is the one thing
    /// an import can take away, and it has to be visible in the preview before it is applied.
    /// </summary>
    [Test]
    public async Task Apply_ReplacesPublisherScopes()
    {
        await Import(s => s.ApplyAsync(new CatalogImportDocument
        {
            Repositories = [new PublisherImport("OpenShock", "Firmware", ["publish_firmware", "publish_modules"])]
        }));

        var plan = await Import(s => s.PlanAsync(new CatalogImportDocument
        {
            Repositories = [new PublisherImport("OpenShock", "Firmware", ["publish_firmware"])]
        }));

        await Assert.That(plan.Updates).IsEqualTo(1);

        await Import(s => s.ApplyAsync(new CatalogImportDocument
        {
            Repositories = [new PublisherImport("OpenShock", "Firmware", ["publish_firmware"])]
        }));

        await Factory.UseAsync(async (RepoServerContext db) =>
        {
            var repository = await db.Repositories.FirstAsync();
            await Assert.That(repository.Scopes).IsEquivalentTo(new[] { RepositoryScope.PublishFirmware });
            return true;
        });
    }

    /// <summary>
    /// Owner and repo are matched case-insensitively, so a file written with different casing than
    /// the existing row updates it rather than creating a second grant a token would never reach.
    /// </summary>
    [Test]
    public async Task Apply_PublisherCasingDoesNotDuplicate()
    {
        await Import(s => s.ApplyAsync(new CatalogImportDocument
        {
            Repositories = [new PublisherImport("OpenShock", "Firmware", ["publish_firmware"])]
        }));

        await Import(s => s.ApplyAsync(new CatalogImportDocument
        {
            Repositories = [new PublisherImport("openshock", "firmware", ["publish_firmware"])]
        }));

        await Factory.UseAsync(async (RepoServerContext db) =>
        {
            await Assert.That(await db.Repositories.CountAsync()).IsEqualTo(1);
            return true;
        });
    }

    /// <summary>
    /// Datasheets write ids in hex and the admin pages show them in hex, but a file written from a
    /// script may carry decimals. Both have to land on the same device.
    /// </summary>
    [Test]
    [Arguments("303A", "1001")]
    [Arguments("0x303A", "0x1001")]
    [Arguments("12346", "4097")]
    public async Task Apply_UsbIdsAcceptHexAndDecimal(string vid, string pid)
    {
        await Import(s => s.ApplyAsync(new CatalogImportDocument
        {
            UsbDevices = [new UsbDeviceImport(vid, pid, "Espressif native USB")]
        }));

        await Factory.UseAsync(async (RepoServerContext db) =>
        {
            var device = await db.UsbDevices.FirstAsync();
            await Assert.That(device.Vid).IsEqualTo(0x303A);
            await Assert.That(device.Pid).IsEqualTo(0x1001);
            return true;
        });
    }

    /// <summary>
    /// A module with no repository is closed to every publisher rather than open to any, so an
    /// import that omits the reference must not invent one.
    /// </summary>
    [Test]
    public async Task Apply_ModuleWithoutRepository_IsUnassigned()
    {
        await Import(s => s.ApplyAsync(new CatalogImportDocument
        {
            Modules = [new ModuleImport("forzashock", "ForzaShock", "Forza Horizon integration.")]
        }));

        await Factory.UseAsync(async (RepoServerContext db) =>
        {
            var module = await db.Modules.FirstAsync();
            await Assert.That(module.Id).IsEqualTo("forzashock");
            await Assert.That(module.RepositoryId).IsNull();
            return true;
        });
    }

    /// <summary>
    /// Modules reference their publisher by owner/repo, which the import resolves to an id — the
    /// file cannot know one, and the same file has to work against any instance.
    /// </summary>
    [Test]
    public async Task Apply_ModuleResolvesRepositoryDeclaredInSameDocument()
    {
        var result = await Import(s => s.ApplyAsync(new CatalogImportDocument
        {
            Repositories = [new PublisherImport("OpenShock", "Desktop-Modules", ["publish_modules"])],
            Modules =
            [
                new ModuleImport("forzashock", "ForzaShock", "Forza Horizon integration.",
                    Repository: "OpenShock/Desktop-Modules")
            ]
        }));

        await Assert.That(result.Blocked).IsEqualTo(0);

        await Factory.UseAsync(async (RepoServerContext db) =>
        {
            var module = await db.Modules.FirstAsync();
            var repository = await db.Repositories.FirstAsync();
            await Assert.That(module.RepositoryId).IsEqualTo(repository.Id);
            return true;
        });
    }

    /// <summary>
    /// Attachments need both sides to exist, and both may be created by the same file, so they are
    /// applied after everything else rather than inline with the chip.
    /// </summary>
    [Test]
    public async Task Apply_AttachesUsbDeviceDeclaredInSameDocument()
    {
        var result = await Import(s => s.ApplyAsync(new CatalogImportDocument
        {
            UsbDevices = [new UsbDeviceImport("303A", "1001", "Espressif native USB")],
            Chips = [new ChipImport("ESP32-S3", "xtensa", ["303A:1001"])]
        }));

        await Assert.That(result.Blocked).IsEqualTo(0);

        await Factory.UseAsync(async (RepoServerContext db) =>
        {
            await Assert.That(await db.FirmwareChipUsbDevices.CountAsync()).IsEqualTo(1);
            return true;
        });
    }
}
