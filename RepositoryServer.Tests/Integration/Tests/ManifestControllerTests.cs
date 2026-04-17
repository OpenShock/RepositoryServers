using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using OpenShock.RepositoryServer.Enums;
using OpenShock.RepositoryServer.RepoServerDb;

namespace OpenShock.RepositoryServer.Tests.Integration.Tests;

[NotInParallel("repo-server-integration")]
public class ManifestControllerTests
{
    [ClassDataSource<WebApplicationFactory>(Shared = SharedType.PerTestSession)]
    public required WebApplicationFactory Factory { get; init; }

    [Before(Test)]
    public Task Setup() => Factory.ResetDatabaseAsync();

    [Test]
    public async Task GetManifest_EmptyDatabase_ReturnsShellWithFixedChannels()
    {
        using var client = Factory.CreateClient();

        var response = await client.GetAsync("/v2/firmware/manifest");
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        var manifest = await response.Content.ReadFromJsonAsync<JsonElement>();

        var channels = manifest.GetProperty("channels").EnumerateArray()
            .Select(e => e.GetString()).ToList();
        await Assert.That(channels).Contains("stable");
        await Assert.That(channels).Contains("beta");
        await Assert.That(channels).Contains("develop");

        var latest = manifest.GetProperty("latest");
        await Assert.That(latest.EnumerateObject().Any()).IsFalse();

        await Assert.That(manifest.GetProperty("boards").GetArrayLength()).IsEqualTo(0);
        await Assert.That(manifest.GetProperty("chips").GetArrayLength()).IsEqualTo(0);
        await Assert.That(manifest.GetProperty("usbSerialFilters").GetArrayLength()).IsEqualTo(0);
        await Assert.That(manifest.GetProperty("usbDevices").GetArrayLength()).IsEqualTo(0);
        await Assert.That(manifest.GetProperty("advisories").GetArrayLength()).IsEqualTo(0);
    }

    [Test]
    public async Task GetManifest_WithSeededData_ReturnsFullShape()
    {
        // Seed directly through EF so we don't rely on admin endpoints being under test.
        Guid chipId;
        Guid boardId;
        Guid usbDeviceId;

        await using (var scope = Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RepoServerContext>();

            var chip = new FirmwareChip
            {
                Id = Guid.NewGuid(),
                Name = "ESP32-S3",
                Architecture = FirmwareChipArchitecture.Xtensa
            };
            var board = new FirmwareBoard
            {
                Id = Guid.NewGuid(),
                Name = "OpenShock Core V1",
                ChipId = chip.Id,
                Discontinued = false,
                RequiredArtifactTypes = [FirmwareArtifactType.Merged]
            };
            var usbDevice = new UsbDevice
            {
                Id = Guid.NewGuid(),
                Vid = 0x1A86,
                Pid = 0x7522,
                Name = "CH9102"
            };
            var filter = new UsbSerialFilter
            {
                Id = Guid.NewGuid(),
                Vid = 0x1A86,
                Pid = null,
                Description = "WCH vendor-wide"
            };
            var advisory = new FirmwareAdvisory
            {
                Id = Guid.NewGuid(),
                Severity = AdvisorySeverity.Critical,
                Title = "OTA bricking bug",
                Content = "Versions before 1.4.0 have a bug that can brick the device during OTA.",
                AffectedVersions = "<1.4.0",
                Url = "https://example.invalid/issues/123"
            };

            db.FirmwareChips.Add(chip);
            db.FirmwareBoards.Add(board);
            db.UsbDevices.Add(usbDevice);
            db.UsbSerialFilters.Add(filter);
            db.FirmwareAdvisories.Add(advisory);
            db.FirmwareBoardUsbDevices.Add(new FirmwareBoardUsbDevice
            {
                BoardId = board.Id,
                UsbDeviceId = usbDevice.Id
            });

            await db.SaveChangesAsync();

            chipId = chip.Id;
            boardId = board.Id;
            usbDeviceId = usbDevice.Id;
        }

        using var client = Factory.CreateClient();
        var response = await client.GetAsync("/v2/firmware/manifest");
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        var manifest = await response.Content.ReadFromJsonAsync<JsonElement>();

        var boards = manifest.GetProperty("boards").EnumerateArray().ToList();
        await Assert.That(boards).HasCount(1);
        await Assert.That(Guid.Parse(boards[0].GetProperty("id").GetString()!))
            .IsEqualTo(boardId);
        await Assert.That(boards[0].GetProperty("name").GetString()).IsEqualTo("OpenShock Core V1");
        await Assert.That(boards[0].GetProperty("chipName").GetString()).IsEqualTo("ESP32-S3");
        await Assert.That(boards[0].GetProperty("discontinued").GetBoolean()).IsFalse();

        var boardUsb = boards[0].GetProperty("usbDevices").EnumerateArray().ToList();
        await Assert.That(boardUsb).HasCount(1);
        await Assert.That(boardUsb[0].GetProperty("vid").GetInt32()).IsEqualTo(0x1A86);
        await Assert.That(boardUsb[0].GetProperty("pid").GetInt32()).IsEqualTo(0x7522);
        await Assert.That(boardUsb[0].GetProperty("name").GetString()).IsEqualTo("CH9102");

        var chips = manifest.GetProperty("chips").EnumerateArray().ToList();
        await Assert.That(chips).HasCount(1);
        await Assert.That(chips[0].GetProperty("name").GetString()).IsEqualTo("ESP32-S3");
        await Assert.That(chips[0].GetProperty("architecture").GetString()).IsEqualTo("xtensa");

        var filters = manifest.GetProperty("usbSerialFilters").EnumerateArray().ToList();
        await Assert.That(filters).HasCount(1);
        await Assert.That(filters[0].GetProperty("vid").GetInt32()).IsEqualTo(0x1A86);
        var filterHasPid = filters[0].TryGetProperty("pid", out var pidProp) &&
                           pidProp.ValueKind != JsonValueKind.Null;
        await Assert.That(filterHasPid).IsFalse();

        var devices = manifest.GetProperty("usbDevices").EnumerateArray().ToList();
        await Assert.That(devices).HasCount(1);
        await Assert.That(Guid.Parse(devices[0].GetProperty("id").GetString()!))
            .IsEqualTo(usbDeviceId);

        var advisories = manifest.GetProperty("advisories").EnumerateArray().ToList();
        await Assert.That(advisories).HasCount(1);
        await Assert.That(advisories[0].GetProperty("severity").GetString()).IsEqualTo("critical");
        await Assert.That(advisories[0].GetProperty("affectedVersions").GetString()).IsEqualTo("<1.4.0");
    }

    [Test]
    public async Task GetManifest_SendsCacheControlHeader()
    {
        using var client = Factory.CreateClient();
        var response = await client.GetAsync("/v2/firmware/manifest");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(response.Headers.CacheControl).IsNotNull();
        await Assert.That(response.Headers.CacheControl!.Public).IsTrue();
        await Assert.That(response.Headers.CacheControl.MaxAge).IsEqualTo(TimeSpan.FromSeconds(300));
    }
}
