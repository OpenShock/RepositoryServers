using Microsoft.EntityFrameworkCore;
using OneOf.Types;
using OpenShock.RepositoryServer.RepoServerDb;
using OpenShock.RepositoryServer.RepoServerDb.Models;
using OpenShock.RepositoryServer.Services.Admin;

namespace OpenShock.RepositoryServer.Tests.Integration.Tests;

[NotInParallel("repo-server-integration")]
public class UsbDevicesAdminTests
{
    [ClassDataSource<WebApplicationFactory>(Shared = SharedType.PerTestSession)]
    public required WebApplicationFactory Factory { get; init; }

    [Before(Test)]
    public Task Setup() => Factory.ResetDatabaseAsync();

    private Task<T> Catalog<T>(Func<CatalogAdminService, Task<T>> operation) => Factory.UseAsync(operation);

    [Test]
    public async Task Upsert_NewDevice_IsStored()
    {
        var device = await Catalog(s => s.UpsertUsbDeviceAsync(0x1A86, 0x7522, "CH9102"));

        await Assert.That(device.Vid).IsEqualTo(0x1A86);
        await Assert.That(device.Pid).IsEqualTo(0x7522);
        await Assert.That(device.Name).IsEqualTo("CH9102");
    }

    /// <summary>
    /// (vid, pid) is the identity of a device, so saving the same pair renames it. A second row would
    /// make lookups ambiguous and let one of them silently win.
    /// </summary>
    [Test]
    public async Task Upsert_DuplicateVidPid_UpdatesNameInPlace()
    {
        var original = await Catalog(s => s.UpsertUsbDeviceAsync(0x10C4, 0xEA60, "CP2102"));
        var updated = await Catalog(s => s.UpsertUsbDeviceAsync(0x10C4, 0xEA60, "CP2102 (Silabs USB-UART)"));

        await Assert.That(updated.Id).IsEqualTo(original.Id);
        await Assert.That(updated.Name).IsEqualTo("CP2102 (Silabs USB-UART)");

        var count = await Factory.UseDbAsync(db => db.UsbDevices.CountAsync());
        await Assert.That(count).IsEqualTo(1);
    }

    [Test]
    public async Task Delete_Unreferenced_Succeeds()
    {
        var device = await Catalog(s => s.UpsertUsbDeviceAsync(0x0403, 0x6001, "FT232"));

        var result = await Catalog(s => s.DeleteUsbDeviceAsync(device.Id));
        result.ShouldBe<Success>();

        var remaining = await Factory.UseDbAsync(db => db.UsbDevices.CountAsync());
        await Assert.That(remaining).IsEqualTo(0);
    }

    [Test]
    public async Task Delete_UnknownId_ReportsNotFound()
    {
        var result = await Catalog(s => s.DeleteUsbDeviceAsync(Guid.NewGuid()));

        result.ShouldBe<NotFound>();
    }

    [Test]
    public async Task Delete_AttachedToChip_ReportsInUse()
    {
        var chip = (await Catalog(s => s.CreateChipAsync("ESP32-S3", null))).ShouldBe<FirmwareChip>();
        var device = await Catalog(s => s.UpsertUsbDeviceAsync(0x303A, 0x1001, "ESP32-S3 USB-JTAG"));
        (await Catalog(s => s.AttachUsbDeviceToChipAsync(chip.Id, device.Id))).ShouldBe<Success>();

        var result = await Catalog(s => s.DeleteUsbDeviceAsync(device.Id));

        result.ShouldBe<InUse>();
    }

    [Test]
    public async Task Delete_AttachedToBoard_ReportsInUse()
    {
        var chip = (await Catalog(s => s.CreateChipAsync("ESP32", null))).ShouldBe<FirmwareChip>();
        var board = (await Catalog(s => s.CreateBoardAsync("Wemos-D1-Mini-ESP32", chip.Id, [])))
            .ShouldBe<FirmwareBoard>();
        var device = await Catalog(s => s.UpsertUsbDeviceAsync(0x1A86, 0x7523, "CH340"));
        (await Catalog(s => s.AttachUsbDeviceToBoardAsync(board.Id, device.Id))).ShouldBe<Success>();

        var result = await Catalog(s => s.DeleteUsbDeviceAsync(device.Id));

        result.ShouldBe<InUse>();
    }

    [Test]
    public async Task List_IsOrderedByVidThenPid()
    {
        await Catalog(s => s.UpsertUsbDeviceAsync(0x1A86, 0x7522, "CH9102"));
        await Catalog(s => s.UpsertUsbDeviceAsync(0x10C4, 0xEA60, "CP2102"));
        await Catalog(s => s.UpsertUsbDeviceAsync(0x0403, 0x6001, "FT232"));

        var devices = await Catalog(s => s.ListUsbDevicesAsync());

        await Assert.That(devices.Length).IsEqualTo(3);
        await Assert.That(devices[0].Vid).IsEqualTo(0x0403);
        await Assert.That(devices[1].Vid).IsEqualTo(0x10C4);
        await Assert.That(devices[2].Vid).IsEqualTo(0x1A86);
    }

    /// <summary>Attaching twice is how a UI resubmit behaves; it must not create a second join row.</summary>
    [Test]
    public async Task AttachToChip_Twice_IsIdempotent()
    {
        var chip = (await Catalog(s => s.CreateChipAsync("ESP32-C3", null))).ShouldBe<FirmwareChip>();
        var device = await Catalog(s => s.UpsertUsbDeviceAsync(0x303A, 0x1001, "USB-JTAG"));

        (await Catalog(s => s.AttachUsbDeviceToChipAsync(chip.Id, device.Id))).ShouldBe<Success>();
        (await Catalog(s => s.AttachUsbDeviceToChipAsync(chip.Id, device.Id))).ShouldBe<Success>();

        var joins = await Factory.UseDbAsync(db => db.FirmwareChipUsbDevices.CountAsync());
        await Assert.That(joins).IsEqualTo(1);
    }

    /// <summary>
    /// Reported as an unresolvable reference rather than a plain not-found, so the caller can say which
    /// of the two sides was missing.
    /// </summary>
    [Test]
    public async Task AttachToChip_UnknownDevice_ReportsMissingReference()
    {
        var chip = (await Catalog(s => s.CreateChipAsync("ESP32-S2", null))).ShouldBe<FirmwareChip>();

        var result = await Catalog(s => s.AttachUsbDeviceToChipAsync(chip.Id, Guid.NewGuid()));

        var missing = result.ShouldBe<ReferenceNotFound>();
        await Assert.That(missing.Reference).IsEqualTo("usb device");
    }

    [Test]
    public async Task DetachFromChip_RemovesTheJoin()
    {
        var chip = (await Catalog(s => s.CreateChipAsync("ESP32", null))).ShouldBe<FirmwareChip>();
        var device = await Catalog(s => s.UpsertUsbDeviceAsync(0x1A86, 0x7522, "CH9102"));
        (await Catalog(s => s.AttachUsbDeviceToChipAsync(chip.Id, device.Id))).ShouldBe<Success>();

        await Catalog(s => s.DetachUsbDeviceFromChipAsync(chip.Id, device.Id));

        var joins = await Factory.UseDbAsync(db => db.FirmwareChipUsbDevices.CountAsync());
        await Assert.That(joins).IsEqualTo(0);
    }
}
