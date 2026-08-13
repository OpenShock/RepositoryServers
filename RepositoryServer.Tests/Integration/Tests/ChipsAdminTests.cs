using Microsoft.EntityFrameworkCore;
using OneOf.Types;
using OpenShock.RepositoryServer.Enums;
using OpenShock.RepositoryServer.RepoServerDb;
using OpenShock.RepositoryServer.RepoServerDb.Models;
using OpenShock.RepositoryServer.Services.Admin;

namespace OpenShock.RepositoryServer.Tests.Integration.Tests;

[NotInParallel("repo-server-integration")]
public class ChipsAdminTests
{
    [ClassDataSource<WebApplicationFactory>(Shared = SharedType.PerTestSession)]
    public required WebApplicationFactory Factory { get; init; }

    [Before(Test)]
    public Task Setup() => Factory.ResetDatabaseAsync();

    private Task<T> Catalog<T>(Func<CatalogAdminService, Task<T>> operation) => Factory.UseAsync(operation);

    [Test]
    public async Task Create_StoresChip()
    {
        var result = await Catalog(s => s.CreateChipAsync("ESP32-S3", FirmwareChipArchitecture.Xtensa));

        var chip = result.ShouldBe<FirmwareChip>();
        await Assert.That(chip.Id).IsNotEqualTo(Guid.Empty);
        await Assert.That(chip.Name).IsEqualTo("ESP32-S3");
        await Assert.That(chip.Architecture).IsEqualTo(FirmwareChipArchitecture.Xtensa);
    }

    [Test]
    public async Task Create_ArchitectureIsOptional()
    {
        var result = await Catalog(s => s.CreateChipAsync("ESP32-H2", null));

        var chip = result.ShouldBe<FirmwareChip>();
        await Assert.That(chip.Architecture).IsNull();
    }

    /// <summary>
    /// Names are the public identity of a chip and are matched case-insensitively, so two spellings
    /// must not be able to coexist with resolution silently picking one.
    /// </summary>
    [Test]
    [Arguments("ESP32-S3")]
    [Arguments("esp32-s3")]
    public async Task Create_DuplicateName_ReportsConflict(string duplicate)
    {
        (await Catalog(s => s.CreateChipAsync("ESP32-S3", FirmwareChipArchitecture.Xtensa)))
            .ShouldBe<FirmwareChip>();

        var result = await Catalog(s => s.CreateChipAsync(duplicate, FirmwareChipArchitecture.Xtensa));

        var conflict = result.ShouldBe<NameConflict>();
        await Assert.That(conflict.Name).IsEqualTo(duplicate);
    }

    [Test]
    public async Task Update_RenamesChip()
    {
        var chip = (await Catalog(s => s.CreateChipAsync("ESP32-S3", FirmwareChipArchitecture.Xtensa)))
            .ShouldBe<FirmwareChip>();

        var result = await Catalog(s =>
            s.UpdateChipAsync(chip.Id, "ESP32-S3 (renamed)", FirmwareChipArchitecture.Xtensa));
        result.ShouldBe<Success>();

        var stored = await Factory.UseDbAsync(db => db.FirmwareChips.FirstAsync(c => c.Id == chip.Id));
        await Assert.That(stored.Name).IsEqualTo("ESP32-S3 (renamed)");
    }

    [Test]
    public async Task Update_UnknownId_ReportsNotFound()
    {
        var result = await Catalog(s => s.UpdateChipAsync(Guid.NewGuid(), "ESP-any", null));

        result.ShouldBe<NotFound>();
    }

    [Test]
    public async Task Update_ToExistingName_ReportsConflict()
    {
        (await Catalog(s => s.CreateChipAsync("ESP32", null))).ShouldBe<FirmwareChip>();
        var second = (await Catalog(s => s.CreateChipAsync("ESP32-C3", null))).ShouldBe<FirmwareChip>();

        var result = await Catalog(s => s.UpdateChipAsync(second.Id, "ESP32", null));

        result.ShouldBe<NameConflict>();
    }

    [Test]
    public async Task Delete_Unused_Succeeds()
    {
        var chip = (await Catalog(s => s.CreateChipAsync("ESP32-C6", FirmwareChipArchitecture.RiscV)))
            .ShouldBe<FirmwareChip>();

        var result = await Catalog(s => s.DeleteChipAsync(chip.Id));
        result.ShouldBe<Success>();

        var remaining = await Factory.UseDbAsync(db => db.FirmwareChips.CountAsync());
        await Assert.That(remaining).IsEqualTo(0);
    }

    [Test]
    public async Task Delete_UnknownId_ReportsNotFound()
    {
        var result = await Catalog(s => s.DeleteChipAsync(Guid.NewGuid()));

        result.ShouldBe<NotFound>();
    }

    [Test]
    public async Task Delete_ReferencedByBoard_ReportsInUse()
    {
        var chip = (await Catalog(s => s.CreateChipAsync("ESP32", FirmwareChipArchitecture.Xtensa)))
            .ShouldBe<FirmwareChip>();
        (await Catalog(s => s.CreateBoardAsync("Wemos-D1-Mini-ESP32", chip.Id, [])))
            .ShouldBe<FirmwareBoard>();

        var result = await Catalog(s => s.DeleteChipAsync(chip.Id));

        var inUse = result.ShouldBe<InUse>();
        await Assert.That(inUse.ReferencedBy).IsEqualTo("boards");
    }

    [Test]
    public async Task AttachUsbDevice_UnknownChip_ReportsMissingReference()
    {
        var result = await Catalog(s => s.AttachUsbDeviceToChipAsync(Guid.NewGuid(), Guid.NewGuid()));

        var missing = result.ShouldBe<ReferenceNotFound>();
        await Assert.That(missing.Reference).IsEqualTo("chip");
    }

    [Test]
    public async Task List_IsOrderedByName()
    {
        (await Catalog(s => s.CreateChipAsync("ESP32-S3", null))).ShouldBe<FirmwareChip>();
        (await Catalog(s => s.CreateChipAsync("ESP32", null))).ShouldBe<FirmwareChip>();
        (await Catalog(s => s.CreateChipAsync("ESP32-C3", null))).ShouldBe<FirmwareChip>();

        var chips = await Catalog(s => s.ListChipsAsync());

        await Assert.That(chips.Select(c => c.Name).ToArray())
            .IsEquivalentTo(new[] { "ESP32", "ESP32-C3", "ESP32-S3" });
    }
}
