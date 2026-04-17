using System.Net;
using System.Net.Http.Json;
using OpenShock.RepositoryServer.Models.Firmware;

namespace OpenShock.RepositoryServer.Tests.Integration.Tests;

[NotInParallel("repo-server-integration")]
public class UsbSerialFiltersAdminControllerTests
{
    private const string BasePath = "/v2/firmware/admin/usb-serial-filters";

    [ClassDataSource<WebApplicationFactory>(Shared = SharedType.PerTestSession)]
    public required WebApplicationFactory Factory { get; init; }

    [Before(Test)]
    public Task Setup() => Factory.ResetDatabaseAsync();

    [Test]
    public async Task Put_VendorWide_Returns201()
    {
        using var client = Factory.CreateAdminClient();
        var response = await client.PutAsJsonAsync(BasePath, new UpsertUsbSerialFilterRequest
        {
            Vid = 0x1A86,
            Description = "WCH vendor-wide"
        });

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<FirmwareUsbSerialFilterAdminDto>();
        await Assert.That(body).IsNotNull();
        await Assert.That(body!.Vid).IsEqualTo(0x1A86);
        await Assert.That(body.Pid).IsNull();
        await Assert.That(body.Description).IsEqualTo("WCH vendor-wide");
    }

    [Test]
    public async Task Put_SpecificVidPid_Returns201WithBothFieldsSet()
    {
        using var client = Factory.CreateAdminClient();
        var response = await client.PutAsJsonAsync(BasePath, new UpsertUsbSerialFilterRequest
        {
            Vid = 0x303A,
            Pid = 0x1001,
            Description = "ESP32-S3 native USB-JTAG"
        });

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<FirmwareUsbSerialFilterAdminDto>();
        await Assert.That(body!.Vid).IsEqualTo(0x303A);
        await Assert.That(body.Pid).IsEqualTo(0x1001);
    }

    [Test]
    public async Task Put_SameVendorWide_UpdatesDescriptionInPlace()
    {
        using var client = Factory.CreateAdminClient();
        var first = await client.PutAsJsonAsync(BasePath, new UpsertUsbSerialFilterRequest
        {
            Vid = 0x0403,
            Description = "FTDI"
        });
        var original = await first.Content.ReadFromJsonAsync<FirmwareUsbSerialFilterAdminDto>();

        var second = await client.PutAsJsonAsync(BasePath, new UpsertUsbSerialFilterRequest
        {
            Vid = 0x0403,
            Description = "FTDI (all products)"
        });

        await Assert.That(second.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var updated = await second.Content.ReadFromJsonAsync<FirmwareUsbSerialFilterAdminDto>();
        await Assert.That(updated).IsNotNull();
        await Assert.That(updated!.Id).IsEqualTo(original!.Id);
        await Assert.That(updated.Description).IsEqualTo("FTDI (all products)");
    }

    [Test]
    public async Task Get_ReturnsAllFilters()
    {
        using var client = Factory.CreateAdminClient();
        await client.PutAsJsonAsync(BasePath, new UpsertUsbSerialFilterRequest { Vid = 0x1A86 });
        await client.PutAsJsonAsync(BasePath, new UpsertUsbSerialFilterRequest { Vid = 0x10C4 });
        await client.PutAsJsonAsync(BasePath, new UpsertUsbSerialFilterRequest { Vid = 0x303A, Pid = 0x1001 });

        var body = await client.GetFromJsonAsync<List<FirmwareUsbSerialFilterAdminDto>>(BasePath);
        await Assert.That(body).IsNotNull();
        await Assert.That(body!).HasCount(3);
    }

    [Test]
    public async Task Delete_ExistingFilter_Returns204()
    {
        using var client = Factory.CreateAdminClient();
        var created = await client.PutAsJsonAsync(BasePath, new UpsertUsbSerialFilterRequest
        {
            Vid = 0x239A
        });
        var body = await created.Content.ReadFromJsonAsync<FirmwareUsbSerialFilterAdminDto>();

        var response = await client.DeleteAsync($"{BasePath}/{body!.Id}");
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
    }

    [Test]
    public async Task Delete_UnknownId_Returns404()
    {
        using var client = Factory.CreateAdminClient();
        var response = await client.DeleteAsync($"{BasePath}/{Guid.NewGuid()}");
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task ManifestShape_OmitsPidForVendorWide()
    {
        using var admin = Factory.CreateAdminClient();
        await admin.PutAsJsonAsync(BasePath, new UpsertUsbSerialFilterRequest
        {
            Vid = 0x1A86
        });

        using var client = Factory.CreateClient();
        var response = await client.GetAsync("/v2/firmware/manifest");
        var json = await response.Content.ReadAsStringAsync();

        // Vendor-wide entries must have no `pid` key on the public shape.
        await Assert.That(json.Contains("\"vid\":6790")).IsTrue();
    }
}
