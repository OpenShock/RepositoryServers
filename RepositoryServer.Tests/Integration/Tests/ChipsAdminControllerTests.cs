using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using OpenShock.RepositoryServer.Enums;
using OpenShock.RepositoryServer.Models.Firmware;
using OpenShock.RepositoryServer.RepoServerDb;

namespace OpenShock.RepositoryServer.Tests.Integration.Tests;

[NotInParallel("repo-server-integration")]
public class ChipsAdminControllerTests
{
    private const string BasePath = "/v2/firmware/admin/chips";

    [ClassDataSource<WebApplicationFactory>(Shared = SharedType.PerTestSession)]
    public required WebApplicationFactory Factory { get; init; }

    [Before(Test)]
    public Task Setup() => Factory.ResetDatabaseAsync();

    [Test]
    public async Task Post_ValidRequest_Returns201WithId()
    {
        using var client = Factory.CreateAdminClient();

        var response = await client.PostAsJsonAsync(BasePath, new CreateFirmwareChipRequest
        {
            Name = "ESP32-S3",
            Architecture = "xtensa"
        });

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var id = body.GetProperty("id").GetGuid();
        await Assert.That(id).IsNotEqualTo(Guid.Empty);
    }

    [Test]
    public async Task Post_InvalidArchitecture_Returns400()
    {
        using var client = Factory.CreateAdminClient();
        var response = await client.PostAsJsonAsync(BasePath, new CreateFirmwareChipRequest
        {
            Name = "ESP42",
            Architecture = "quantum"
        });
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task Put_ExistingChip_UpdatesName()
    {
        var id = await SeedChipAsync(name: "ESP32-S3", arch: FirmwareChipArchitecture.Xtensa);
        using var client = Factory.CreateAdminClient();

        var response = await client.PutAsJsonAsync($"{BasePath}/{id}", new CreateFirmwareChipRequest
        {
            Name = "ESP32-S3 (renamed)",
            Architecture = "xtensa"
        });
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        await using var scope = Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<RepoServerContext>();
        var chip = await db.FirmwareChips.FindAsync(id);
        await Assert.That(chip!.Name).IsEqualTo("ESP32-S3 (renamed)");
    }

    [Test]
    public async Task Put_UnknownId_Returns404()
    {
        using var client = Factory.CreateAdminClient();
        var response = await client.PutAsJsonAsync($"{BasePath}/{Guid.NewGuid()}",
            new CreateFirmwareChipRequest { Name = "ESP-any" });
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task Delete_Unused_Returns204()
    {
        var id = await SeedChipAsync(name: "ESP32-C6", arch: FirmwareChipArchitecture.RiscV);
        using var client = Factory.CreateAdminClient();

        var response = await client.DeleteAsync($"{BasePath}/{id}");
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
    }

    [Test]
    public async Task Delete_ReferencedByBoard_Returns409InUse()
    {
        var chipId = await SeedChipAsync(name: "ESP32", arch: FirmwareChipArchitecture.Xtensa);
        await using (var scope = Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RepoServerContext>();
            db.FirmwareBoards.Add(new FirmwareBoard
            {
                Id = Guid.NewGuid(),
                Name = "Wemos-D1-Mini-ESP32",
                ChipId = chipId,
                RequiredArtifactTypes = []
            });
            await db.SaveChangesAsync();
        }

        using var client = Factory.CreateAdminClient();
        var response = await client.DeleteAsync($"{BasePath}/{chipId}");
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
    }

    [Test]
    public async Task AttachUsbDevice_Idempotent()
    {
        var chipId = await SeedChipAsync(name: "ESP32-S3", arch: FirmwareChipArchitecture.Xtensa);
        Guid deviceId;
        await using (var scope = Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RepoServerContext>();
            var device = new UsbDevice { Id = Guid.NewGuid(), Vid = 0x303A, Pid = 0x1001, Name = "ESP32-S3 USB-JTAG" };
            db.UsbDevices.Add(device);
            await db.SaveChangesAsync();
            deviceId = device.Id;
        }

        using var client = Factory.CreateAdminClient();
        var first = await client.PutAsync($"{BasePath}/{chipId}/usb-devices/{deviceId}", content: null);
        var second = await client.PutAsync($"{BasePath}/{chipId}/usb-devices/{deviceId}", content: null);

        await Assert.That(first.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
        await Assert.That(second.StatusCode).IsEqualTo(HttpStatusCode.NoContent);

        await using var verify = Factory.Services.CreateAsyncScope();
        var db2 = verify.ServiceProvider.GetRequiredService<RepoServerContext>();
        var count = db2.FirmwareChipUsbDevices.Count(j => j.ChipId == chipId && j.UsbDeviceId == deviceId);
        await Assert.That(count).IsEqualTo(1);
    }

    [Test]
    public async Task AttachUsbDevice_UnknownChip_Returns404()
    {
        using var client = Factory.CreateAdminClient();
        var response = await client.PutAsync(
            $"{BasePath}/{Guid.NewGuid()}/usb-devices/{Guid.NewGuid()}", content: null);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }

    private async Task<Guid> SeedChipAsync(string name, FirmwareChipArchitecture arch)
    {
        using var client = Factory.CreateAdminClient();
        var response = await client.PostAsJsonAsync(BasePath, new CreateFirmwareChipRequest
        {
            Name = name,
            Architecture = arch.ToString().ToLowerInvariant()
        });
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("id").GetGuid();
    }
}
