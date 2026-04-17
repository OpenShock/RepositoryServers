using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using OpenShock.RepositoryServer.Models.Firmware;
using OpenShock.RepositoryServer.RepoServerDb;

namespace OpenShock.RepositoryServer.Tests.Integration.Tests;

[NotInParallel("repo-server-integration")]
public class UsbDevicesAdminControllerTests
{
    private const string BasePath = "/v2/firmware/admin/usb-devices";

    [ClassDataSource<WebApplicationFactory>(Shared = SharedType.PerTestSession)]
    public required WebApplicationFactory Factory { get; init; }

    [Before(Test)]
    public Task Setup() => Factory.ResetDatabaseAsync();

    [Test]
    public async Task Put_NewDevice_Returns201()
    {
        using var client = Factory.CreateAdminClient();
        var response = await client.PutAsJsonAsync(BasePath, new UpsertUsbDeviceRequest
        {
            Vid = 0x1A86,
            Pid = 0x7522,
            Name = "CH9102"
        });

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<FirmwareUsbDeviceDto>();
        await Assert.That(body).IsNotNull();
        await Assert.That(body!.Vid).IsEqualTo(0x1A86);
        await Assert.That(body.Pid).IsEqualTo(0x7522);
        await Assert.That(body.Name).IsEqualTo("CH9102");
    }

    [Test]
    public async Task Put_DuplicateVidPid_UpdatesNameInPlace()
    {
        using var client = Factory.CreateAdminClient();

        var first = await client.PutAsJsonAsync(BasePath, new UpsertUsbDeviceRequest
        {
            Vid = 0x10C4, Pid = 0xEA60, Name = "CP2102"
        });
        await Assert.That(first.StatusCode).IsEqualTo(HttpStatusCode.Created);
        var original = await first.Content.ReadFromJsonAsync<FirmwareUsbDeviceDto>();

        // Same (vid, pid), new name — should update, reuse id, return 200
        var second = await client.PutAsJsonAsync(BasePath, new UpsertUsbDeviceRequest
        {
            Vid = 0x10C4, Pid = 0xEA60, Name = "CP2102 (Silabs USB-UART)"
        });
        await Assert.That(second.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var updated = await second.Content.ReadFromJsonAsync<FirmwareUsbDeviceDto>();

        await Assert.That(updated).IsNotNull();
        await Assert.That(updated!.Id).IsEqualTo(original!.Id);
        await Assert.That(updated.Name).IsEqualTo("CP2102 (Silabs USB-UART)");
    }

    [Test]
    public async Task Delete_Unreferenced_Returns204()
    {
        using var client = Factory.CreateAdminClient();
        var created = await client.PutAsJsonAsync(BasePath, new UpsertUsbDeviceRequest
        {
            Vid = 0x0403, Pid = 0x6001, Name = "FT232"
        });
        var body = await created.Content.ReadFromJsonAsync<FirmwareUsbDeviceDto>();

        var deleteResponse = await client.DeleteAsync($"{BasePath}/{body!.Id}");
        await Assert.That(deleteResponse.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
    }

    [Test]
    public async Task Delete_UnknownId_Returns404()
    {
        using var client = Factory.CreateAdminClient();
        var response = await client.DeleteAsync($"{BasePath}/{Guid.NewGuid()}");
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task Delete_AttachedToChip_Returns409InUse()
    {
        Guid deviceId;
        await using (var scope = Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RepoServerContext>();
            var chip = new FirmwareChip { Id = Guid.NewGuid(), Name = "ESP32-S3" };
            var device = new UsbDevice { Id = Guid.NewGuid(), Vid = 0x303A, Pid = 0x1001, Name = "ESP32-S3 USB-JTAG" };
            db.FirmwareChips.Add(chip);
            db.UsbDevices.Add(device);
            db.FirmwareChipUsbDevices.Add(new FirmwareChipUsbDevice
            {
                ChipId = chip.Id,
                UsbDeviceId = device.Id
            });
            await db.SaveChangesAsync();
            deviceId = device.Id;
        }

        using var client = Factory.CreateAdminClient();
        var response = await client.DeleteAsync($"{BasePath}/{deviceId}");
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
    }

    [Test]
    public async Task Get_ListsDevicesOrderedByVidPid()
    {
        using var client = Factory.CreateAdminClient();
        await client.PutAsJsonAsync(BasePath, new UpsertUsbDeviceRequest { Vid = 0x1A86, Pid = 0x7522, Name = "CH9102" });
        await client.PutAsJsonAsync(BasePath, new UpsertUsbDeviceRequest { Vid = 0x10C4, Pid = 0xEA60, Name = "CP2102" });
        await client.PutAsJsonAsync(BasePath, new UpsertUsbDeviceRequest { Vid = 0x0403, Pid = 0x6001, Name = "FT232" });

        var devices = await client.GetFromJsonAsync<List<FirmwareUsbDeviceDto>>(BasePath);
        await Assert.That(devices).IsNotNull();
        await Assert.That(devices!).HasCount(3);
        await Assert.That(devices[0].Vid).IsEqualTo(0x0403);
        await Assert.That(devices[1].Vid).IsEqualTo(0x10C4);
        await Assert.That(devices[2].Vid).IsEqualTo(0x1A86);
    }
}
