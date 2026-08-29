using OpenShock.RepositoryServer.Utils;
using Version = OpenShock.RepositoryServer.RepoServerDb.Models.Version;

namespace OpenShock.RepositoryServer.Tests.Utils;

public class ModuleVersionsTests
{
    private static Version Of(string versionName) => new()
    {
        Module = "openshock.overlay",
        VersionName = versionName,
        ZipUrl = new Uri("https://cdn.example.com/module.zip"),
        HashSha256 = new byte[32]
    };

    /// <summary>
    /// The reason this exists: versions are stored as text, and text order puts 10.0.0 below 9.0.0,
    /// which is the wrong answer to "what is current".
    /// </summary>
    [Test]
    public async Task NewestFirst_OrdersNumerically()
    {
        var ordered = ModuleVersions
            .NewestFirst([Of("9.0.0"), Of("10.0.0"), Of("1.2.3")])
            .Select(v => v.VersionName);

        await Assert.That(ordered).IsEquivalentTo(new[] { "10.0.0", "9.0.0", "1.2.3" });
    }

    [Test]
    public async Task NewestFirst_SortsPrereleaseBelowItsRelease()
    {
        var ordered = ModuleVersions
            .NewestFirst([Of("1.0.0-rc.1"), Of("1.0.0")])
            .Select(v => v.VersionName);

        await Assert.That(ordered).IsEquivalentTo(new[] { "1.0.0", "1.0.0-rc.1" });
    }

    /// <summary>
    /// Kept rather than dropped: a row that does not parse still has to be reachable in the UI, which
    /// is the only place it can be withdrawn from.
    /// </summary>
    [Test]
    public async Task NewestFirst_KeepsUnparseableRowsLast()
    {
        var ordered = ModuleVersions
            .NewestFirst([Of("not-a-version"), Of("1.0.0")])
            .Select(v => v.VersionName);

        await Assert.That(ordered).IsEquivalentTo(new[] { "1.0.0", "not-a-version" });
    }

    [Test]
    public async Task Latest_IgnoresUnparseableRows()
    {
        var latest = ModuleVersions.Latest([Of("nightly"), Of("2.1.0"), Of("2.0.0")]);

        await Assert.That(latest).IsEqualTo("2.1.0");
    }

    [Test]
    public async Task Latest_NothingParseable_ReturnsNull()
    {
        var latest = ModuleVersions.Latest([Of("nightly")]);

        await Assert.That(latest).IsNull();
    }
}
