using Microsoft.EntityFrameworkCore;
using OneOf.Types;
using OpenShock.RepositoryServer.RepoServerDb;
using OpenShock.RepositoryServer.Services.Admin;
using Module = OpenShock.RepositoryServer.RepoServerDb.Models.Module;
using Version = OpenShock.RepositoryServer.RepoServerDb.Models.Version;

namespace OpenShock.RepositoryServer.Tests.Integration.Tests;

/// <summary>
/// The admin surface behind the desktop module pages. Publishing normally runs through CI/CD, which
/// uploads a zip and refuses to touch a version that exists; these operations are the administrative
/// override, so what they must not do is accept a row the index cannot serve.
/// </summary>
[NotInParallel("repo-server-integration")]
public class ModulesAdminTests
{
    private const string ModuleId = "openshock.overlay";

    private static readonly Uri ZipUrl = new("https://cdn.example.com/modules/overlay/1.0.0/module.zip");
    private static readonly byte[] Digest = Enumerable.Repeat((byte)0xAB, 32).ToArray();

    [ClassDataSource<WebApplicationFactory>(Shared = SharedType.PerTestSession)]
    public required WebApplicationFactory Factory { get; init; }

    [Before(Test)]
    public Task Setup() => Factory.ResetDatabaseAsync();

    private Task<T> Modules<T>(Func<ModuleAdminService, Task<T>> operation) => Factory.UseAsync(operation);

    private async Task SeedModuleAsync() =>
        (await Modules(s => s.UpsertAsync(ModuleId, "Overlay", "The overlay", null, null, null)))
            .ShouldBe<Module>();

    [Test]
    public async Task UpsertVersion_NewVersion_IsRegistered()
    {
        await SeedModuleAsync();

        var version = (await Modules(s => s.UpsertVersionAsync(
            ModuleId, "1.0.0", ZipUrl, Digest, null, null))).ShouldBe<Version>();

        await Assert.That(version.VersionName).IsEqualTo("1.0.0");
        await Assert.That(version.ZipUrl).IsEqualTo(ZipUrl);
        await Assert.That(version.HashSha256).IsEquivalentTo(Digest);
    }

    /// <summary>
    /// The publish endpoint refuses to replace a version because clients have already seen the zip.
    /// An admin correcting the row it wrote is the one case where that has to be possible.
    /// </summary>
    [Test]
    public async Task UpsertVersion_ExistingVersion_CorrectsItInPlace()
    {
        await SeedModuleAsync();
        (await Modules(s => s.UpsertVersionAsync(ModuleId, "1.0.0", ZipUrl, Digest, null, null)))
            .ShouldBe<Version>();

        var changelog = new Uri("https://github.com/OpenShock/desktop/releases/tag/v1.0.0");
        var corrected = (await Modules(s => s.UpsertVersionAsync(
            ModuleId, "1.0.0", ZipUrl, Digest, changelog, null))).ShouldBe<Version>();

        await Assert.That(corrected.ChangelogUrl).IsEqualTo(changelog);

        var count = await Factory.UseDbAsync(db => db.Versions.CountAsync());
        await Assert.That(count).IsEqualTo(1);
    }

    [Test]
    public async Task UpsertVersion_MixedCase_IsNormalized()
    {
        await SeedModuleAsync();

        var version = (await Modules(s => s.UpsertVersionAsync(
            ModuleId.ToUpperInvariant(), "1.0.0-RC.1", ZipUrl, Digest, null, null))).ShouldBe<Version>();

        // Publishing lowercases both, and the two paths have to agree or an admin-entered row is a
        // second row rather than the same one.
        await Assert.That(version.Module).IsEqualTo(ModuleId);
        await Assert.That(version.VersionName).IsEqualTo("1.0.0-rc.1");
    }

    /// <summary>
    /// The repository index parses every stored version to build itself, so a row that does not parse
    /// takes down the whole index rather than only itself.
    /// </summary>
    [Test]
    public async Task UpsertVersion_NotSemver_IsRejected()
    {
        await SeedModuleAsync();

        var result = await Modules(s => s.UpsertVersionAsync(
            ModuleId, "nightly", ZipUrl, Digest, null, null));

        result.ShouldBe<InvalidVersion>();
        await Assert.That(await Factory.UseDbAsync(db => db.Versions.CountAsync())).IsEqualTo(0);
    }

    [Test]
    public async Task UpsertVersion_DigestOfTheWrongLength_IsRejected()
    {
        await SeedModuleAsync();

        var result = await Modules(s => s.UpsertVersionAsync(
            ModuleId, "1.0.0", ZipUrl, [0xAB, 0xCD], null, null));

        result.ShouldBe<InvalidHash>();
        await Assert.That(await Factory.UseDbAsync(db => db.Versions.CountAsync())).IsEqualTo(0);
    }

    [Test]
    public async Task UpsertVersion_UnknownModule_ReportsReferenceNotFound()
    {
        var result = await Modules(s => s.UpsertVersionAsync(
            "no.such.module", "1.0.0", ZipUrl, Digest, null, null));

        result.ShouldBe<ReferenceNotFound>();
    }

    [Test]
    public async Task Find_ReturnsTheModuleWithItsVersions()
    {
        await SeedModuleAsync();
        (await Modules(s => s.UpsertVersionAsync(ModuleId, "1.0.0", ZipUrl, Digest, null, null)))
            .ShouldBe<Version>();

        // The module page renders the versions off this one read, so they have to come with it.
        var module = await Modules(s => s.FindAsync(ModuleId));

        await Assert.That(module).IsNotNull();
        await Assert.That(module!.Versions.Count).IsEqualTo(1);
    }

    [Test]
    public async Task DeleteVersion_RemovesOnlyThatVersion()
    {
        await SeedModuleAsync();
        (await Modules(s => s.UpsertVersionAsync(ModuleId, "1.0.0", ZipUrl, Digest, null, null)))
            .ShouldBe<Version>();
        (await Modules(s => s.UpsertVersionAsync(ModuleId, "1.1.0", ZipUrl, Digest, null, null)))
            .ShouldBe<Version>();

        (await Modules(s => s.DeleteVersionAsync(ModuleId, "1.0.0"))).ShouldBe<Success>();

        var remaining = await Factory.UseDbAsync(db =>
            db.Versions.Select(v => v.VersionName).ToArrayAsync());
        await Assert.That(remaining).IsEquivalentTo(new[] { "1.1.0" });
    }
}
