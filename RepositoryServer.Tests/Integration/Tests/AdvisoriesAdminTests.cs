using Microsoft.EntityFrameworkCore;
using OneOf.Types;
using OpenShock.RepositoryServer.Enums;
using OpenShock.RepositoryServer.Services.Admin;

namespace OpenShock.RepositoryServer.Tests.Integration.Tests;

[NotInParallel("repo-server-integration")]
public class AdvisoriesAdminTests
{
    [ClassDataSource<WebApplicationFactory>(Shared = SharedType.PerTestSession)]
    public required WebApplicationFactory Factory { get; init; }

    [Before(Test)]
    public Task Setup() => Factory.ResetDatabaseAsync();

    private Task<T> Advisories<T>(Func<AdvisoryAdminService, Task<T>> operation) => Factory.UseAsync(operation);

    [Test]
    public async Task Create_StoresAdvisory()
    {
        var advisory = await Advisories(s => s.CreateAsync(
            AdvisorySeverity.Critical,
            "OTA bricking bug",
            "Versions before 1.4.0 have a bug.",
            "<1.4.0",
            "https://example.invalid/issues/123"));

        await Assert.That(advisory.Id).IsNotEqualTo(Guid.Empty);
        await Assert.That(advisory.Severity).IsEqualTo(AdvisorySeverity.Critical);
        await Assert.That(advisory.Title).IsEqualTo("OTA bricking bug");
    }

    [Test]
    public async Task Create_UrlIsOptional()
    {
        var advisory = await Advisories(s => s.CreateAsync(
            AdvisorySeverity.Info, "No link", "Body", ">=1.0.0", null));

        await Assert.That(advisory.Url).IsNull();
    }

    /// <summary>
    /// Postgres orders enum columns by label creation order, which is alphabetical here and unrelated
    /// to how serious an advisory is, so the ordering has to happen on the CLR enum.
    /// </summary>
    [Test]
    public async Task List_IsOrderedBySeverityRank()
    {
        await Advisories(s => s.CreateAsync(AdvisorySeverity.Info, "Minor issue", "c", "<1", null));
        await Advisories(s => s.CreateAsync(AdvisorySeverity.Critical, "Bricking bug", "c", "<1", null));
        await Advisories(s => s.CreateAsync(AdvisorySeverity.Warning, "Gotcha", "c", "<1", null));

        var advisories = await Advisories(s => s.ListAsync());

        await Assert.That(advisories.Length).IsEqualTo(3);
        await Assert.That(advisories[0].Severity).IsEqualTo(AdvisorySeverity.Critical);
        await Assert.That(advisories[1].Severity).IsEqualTo(AdvisorySeverity.Warning);
        await Assert.That(advisories[2].Severity).IsEqualTo(AdvisorySeverity.Info);
    }

    [Test]
    public async Task Update_ChangesFields()
    {
        var advisory = await Advisories(s => s.CreateAsync(
            AdvisorySeverity.Info, "Original title", "Original", "<1.0.0", null));

        var result = await Advisories(s => s.UpdateAsync(
            advisory.Id,
            AdvisorySeverity.Warning,
            "Updated title",
            "Updated content",
            ">=1.0.0 <2.0.0",
            "https://example.invalid/new"));
        result.ShouldBe<Success>();

        var stored = await Factory.UseDbAsync(db =>
            db.FirmwareAdvisories.FirstAsync(a => a.Id == advisory.Id));

        await Assert.That(stored.Severity).IsEqualTo(AdvisorySeverity.Warning);
        await Assert.That(stored.Title).IsEqualTo("Updated title");
        await Assert.That(stored.AffectedVersions).IsEqualTo(">=1.0.0 <2.0.0");
    }

    [Test]
    public async Task Update_UnknownId_ReportsNotFound()
    {
        var result = await Advisories(s => s.UpdateAsync(
            Guid.NewGuid(), AdvisorySeverity.Info, "t", "c", "<1", null));

        result.ShouldBe<NotFound>();
    }

    [Test]
    public async Task Delete_Existing_Succeeds()
    {
        var advisory = await Advisories(s => s.CreateAsync(
            AdvisorySeverity.Info, "Going away", "c", "<1", null));

        var result = await Advisories(s => s.DeleteAsync(advisory.Id));
        result.ShouldBe<Success>();

        var remaining = await Factory.UseDbAsync(db => db.FirmwareAdvisories.CountAsync());
        await Assert.That(remaining).IsEqualTo(0);
    }

    [Test]
    public async Task Delete_UnknownId_ReportsNotFound()
    {
        var result = await Advisories(s => s.DeleteAsync(Guid.NewGuid()));

        result.ShouldBe<NotFound>();
    }

    /// <summary>
    /// Advisories exist to warn hubs and the flashtool, so this checks one actually reaches the public
    /// manifest rather than only landing in the table.
    /// </summary>
    [Test]
    public async Task Created_AppearsOnPublicManifest()
    {
        await Advisories(s => s.CreateAsync(
            AdvisorySeverity.Critical, "Bricking bug", "Do not install", "<1.4.0", null));

        using var client = Factory.CreateClient();
        var json = await client.GetStringAsync("/2/firmware/manifest");

        await Assert.That(json).Contains("Bricking bug");
        await Assert.That(json).Contains("critical");
    }
}
