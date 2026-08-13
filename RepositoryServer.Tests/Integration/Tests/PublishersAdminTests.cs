using Microsoft.EntityFrameworkCore;
using OneOf.Types;
using OpenShock.RepositoryServer.Enums;
using OpenShock.RepositoryServer.RepoServerDb;
using OpenShock.RepositoryServer.Services.Admin;

namespace OpenShock.RepositoryServer.Tests.Integration.Tests;

/// <summary>
/// The publish allowlist. Registration here is the authorization decision for CI publishing, so these
/// cover who ends up authorized rather than merely what is stored.
/// </summary>
[NotInParallel("repo-server-integration")]
public class PublishersAdminTests
{
    [ClassDataSource<WebApplicationFactory>(Shared = SharedType.PerTestSession)]
    public required WebApplicationFactory Factory { get; init; }

    [Before(Test)]
    public Task Setup() => Factory.ResetDatabaseAsync();

    private Task<T> Publishers<T>(Func<PublisherAdminService, Task<T>> operation) => Factory.UseAsync(operation);

    [Test]
    public async Task List_EmptyDatabase_ReturnsNothing()
    {
        var repositories = await Publishers(s => s.ListAsync());

        await Assert.That(repositories.Length).IsEqualTo(0);
    }

    [Test]
    public async Task Upsert_RegistersRepository()
    {
        var repository = await Publishers(s => s.UpsertAsync(
            RepositoryProvider.Github, "openshock", "firmware", [RepositoryScope.PublishFirmware]));

        await Assert.That(repository.Id).IsNotEqualTo(Guid.Empty);
        await Assert.That(repository.Owner).IsEqualTo("openshock");
        await Assert.That(repository.Repo).IsEqualTo("firmware");
        await Assert.That(repository.Scopes).Contains(RepositoryScope.PublishFirmware);
    }

    [Test]
    public async Task Upsert_IsIdempotent_SoRerunningOnboardingIsHarmless()
    {
        var first = await Publishers(s => s.UpsertAsync(
            RepositoryProvider.Github, "openshock", "firmware", []));
        var second = await Publishers(s => s.UpsertAsync(
            RepositoryProvider.Github, "openshock", "firmware", []));

        await Assert.That(second.Id).IsEqualTo(first.Id);

        var all = await Publishers(s => s.ListAsync());
        await Assert.That(all.Length).IsEqualTo(1);
    }

    /// <summary>
    /// GitHub treats owner and repo case-insensitively and the OIDC handler resolves them that way, so
    /// a differently cased re-registration has to land on the same grant. A second row would be
    /// unreachable, and the repository would keep whatever scopes the first row had.
    /// </summary>
    [Test]
    public async Task Upsert_DifferentCasing_UpdatesTheSameRegistration()
    {
        var first = await Publishers(s => s.UpsertAsync(
            RepositoryProvider.Github, "openshock", "firmware", [RepositoryScope.PublishFirmware]));

        var second = await Publishers(s => s.UpsertAsync(
            RepositoryProvider.Github, "OpenShock", "Firmware",
            [RepositoryScope.PublishFirmware, RepositoryScope.PublishModules]));

        await Assert.That(second.Id).IsEqualTo(first.Id);
        await Assert.That(second.Scopes).Contains(RepositoryScope.PublishModules);

        var all = await Publishers(s => s.ListAsync());
        await Assert.That(all.Length).IsEqualTo(1);
    }

    /// <summary>Scopes are authoritative on re-registration, which is how a grant is narrowed.</summary>
    [Test]
    public async Task Upsert_ReplacesScopes_SoAGrantCanBeRevoked()
    {
        var repository = await Publishers(s => s.UpsertAsync(
            RepositoryProvider.Github, "openshock", "firmware",
            [RepositoryScope.PublishFirmware, RepositoryScope.PublishModules]));

        await Publishers(s => s.UpsertAsync(
            RepositoryProvider.Github, "openshock", "firmware", []));

        var stored = await Factory.UseDbAsync(db => db.Repositories.FirstAsync(r => r.Id == repository.Id));
        await Assert.That(stored.Scopes.Length).IsEqualTo(0);
    }

    [Test]
    public async Task Upsert_DeduplicatesScopes()
    {
        var repository = await Publishers(s => s.UpsertAsync(
            RepositoryProvider.Github, "openshock", "firmware",
            [RepositoryScope.PublishFirmware, RepositoryScope.PublishFirmware]));

        await Assert.That(repository.Scopes.Length).IsEqualTo(1);
    }

    [Test]
    public async Task Delete_Unreferenced_Succeeds()
    {
        var repository = await Publishers(s => s.UpsertAsync(
            RepositoryProvider.Github, "openshock", "desktop", []));

        var result = await Publishers(s => s.DeleteAsync(repository.Id));

        result.ShouldBe<Success>();
    }

    [Test]
    public async Task Delete_UnknownId_ReportsNotFound()
    {
        var result = await Publishers(s => s.DeleteAsync(Guid.NewGuid()));

        result.ShouldBe<NotFound>();
    }

    /// <summary>
    /// Published versions record which repository produced them. Deleting the row would erase that
    /// provenance, so revocation of a repository that has published means removing its scopes instead.
    /// </summary>
    [Test]
    public async Task Delete_ReferencedByFirmwareVersion_ReportsInUse()
    {
        var repository = await Publishers(s => s.UpsertAsync(
            RepositoryProvider.Github, "openshock", "firmware", [RepositoryScope.PublishFirmware]));

        await Factory.UseDbAsync(async db =>
        {
            db.FirmwareVersions.Add(new FirmwareVersion
            {
                Version = "1.5.1",
                Channel = ReleaseChannel.Stable,
                ReleaseDate = DateTimeOffset.UtcNow,
                RepositoryId = repository.Id,
                CommitHash = "abc1234567890abcdef1234567890abcdef12345"
            });
            await db.SaveChangesAsync();
        });

        var result = await Publishers(s => s.DeleteAsync(repository.Id));

        result.ShouldBe<InUse>();
    }

    [Test]
    public async Task List_IsOrderedByOwnerThenRepo()
    {
        await Publishers(s => s.UpsertAsync(RepositoryProvider.Github, "openshock", "firmware", []));
        await Publishers(s => s.UpsertAsync(RepositoryProvider.Github, "openshock", "desktop", []));
        await Publishers(s => s.UpsertAsync(RepositoryProvider.Github, "acme", "widgets", []));

        var all = await Publishers(s => s.ListAsync());

        await Assert.That(all.Select(r => $"{r.Owner}/{r.Repo}").ToArray())
            .IsEquivalentTo(new[] { "acme/widgets", "openshock/desktop", "openshock/firmware" });
    }
}
