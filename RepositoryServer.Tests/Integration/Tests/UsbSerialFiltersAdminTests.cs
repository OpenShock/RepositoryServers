using OneOf.Types;
using OpenShock.RepositoryServer.Services.Admin;

namespace OpenShock.RepositoryServer.Tests.Integration.Tests;

[NotInParallel("repo-server-integration")]
public class UsbSerialFiltersAdminTests
{
    [ClassDataSource<WebApplicationFactory>(Shared = SharedType.PerTestSession)]
    public required WebApplicationFactory Factory { get; init; }

    [Before(Test)]
    public Task Setup() => Factory.ResetDatabaseAsync();

    private Task<T> Catalog<T>(Func<CatalogAdminService, Task<T>> operation) => Factory.UseAsync(operation);

    [Test]
    public async Task Upsert_VendorWide_LeavesPidNull()
    {
        var filter = await Catalog(s => s.UpsertUsbSerialFilterAsync(0x1A86, null, "WCH vendor-wide"));

        await Assert.That(filter.Vid).IsEqualTo(0x1A86);
        await Assert.That(filter.Pid).IsNull();
        await Assert.That(filter.Description).IsEqualTo("WCH vendor-wide");
    }

    [Test]
    public async Task Upsert_SpecificDevice_SetsBothFields()
    {
        var filter = await Catalog(s =>
            s.UpsertUsbSerialFilterAsync(0x303A, 0x1001, "ESP32-S3 native USB-JTAG"));

        await Assert.That(filter.Vid).IsEqualTo(0x303A);
        await Assert.That(filter.Pid).IsEqualTo(0x1001);
    }

    /// <summary>
    /// The unique index treats nulls as equal, so a second vendor-wide row for the same VID has to
    /// update the first rather than create a duplicate the picker would show twice.
    /// </summary>
    [Test]
    public async Task Upsert_SameVendorWide_UpdatesInPlace()
    {
        var original = await Catalog(s => s.UpsertUsbSerialFilterAsync(0x0403, null, "FTDI"));
        var updated = await Catalog(s => s.UpsertUsbSerialFilterAsync(0x0403, null, "FTDI (all products)"));

        await Assert.That(updated.Id).IsEqualTo(original.Id);
        await Assert.That(updated.Description).IsEqualTo("FTDI (all products)");

        var filters = await Catalog(s => s.ListUsbSerialFiltersAsync());
        await Assert.That(filters.Length).IsEqualTo(1);
    }

    /// <summary>A vendor-wide rule and a specific one for the same VID are different rules.</summary>
    [Test]
    public async Task Upsert_VendorWideAndSpecific_CoexistForSameVid()
    {
        await Catalog(s => s.UpsertUsbSerialFilterAsync(0x1A86, null, "vendor-wide"));
        await Catalog(s => s.UpsertUsbSerialFilterAsync(0x1A86, 0x7522, "CH9102"));

        var filters = await Catalog(s => s.ListUsbSerialFiltersAsync());

        await Assert.That(filters.Length).IsEqualTo(2);
    }

    [Test]
    public async Task List_ReturnsAllFilters()
    {
        await Catalog(s => s.UpsertUsbSerialFilterAsync(0x1A86, null, null));
        await Catalog(s => s.UpsertUsbSerialFilterAsync(0x10C4, null, null));
        await Catalog(s => s.UpsertUsbSerialFilterAsync(0x303A, 0x1001, null));

        var filters = await Catalog(s => s.ListUsbSerialFiltersAsync());

        await Assert.That(filters.Length).IsEqualTo(3);
    }

    [Test]
    public async Task Delete_Existing_Succeeds()
    {
        var filter = await Catalog(s => s.UpsertUsbSerialFilterAsync(0x239A, null, null));

        var result = await Catalog(s => s.DeleteUsbSerialFilterAsync(filter.Id));

        result.ShouldBe<Success>();
    }

    [Test]
    public async Task Delete_UnknownId_ReportsNotFound()
    {
        var result = await Catalog(s => s.DeleteUsbSerialFilterAsync(Guid.NewGuid()));

        result.ShouldBe<NotFound>();
    }

    /// <summary>
    /// Filters reach the flashtool through the public manifest, so this checks the row actually
    /// surfaces there rather than only existing in the table.
    /// </summary>
    [Test]
    public async Task PublishedFilter_AppearsOnPublicManifest()
    {
        await Catalog(s => s.UpsertUsbSerialFilterAsync(0x1A86, null, null));

        using var client = Factory.CreateClient();
        var response = await client.GetAsync("/2/firmware/manifest");
        var json = await response.Content.ReadAsStringAsync();

        await Assert.That(json).Contains("\"vid\":6790");
    }
}
