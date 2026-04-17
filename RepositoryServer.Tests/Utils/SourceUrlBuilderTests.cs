using OpenShock.RepositoryServer.Enums;
using OpenShock.RepositoryServer.Utils;

namespace OpenShock.RepositoryServer.Tests.Utils;

public class SourceUrlBuilderTests
{
    [Test]
    public async Task BuildCommitUrl_Github_ReturnsCanonicalCommitUrl()
    {
        var url = SourceUrlBuilder.BuildCommitUrl(RepositoryProvider.Github, "openshock", "firmware", "21e43623abcdef1234567890abcdef1234567890");
        await Assert.That(url).IsEqualTo("https://github.com/openshock/firmware/commit/21e43623abcdef1234567890abcdef1234567890");
    }

    [Test]
    public async Task BuildCommitUrl_EmptyHash_ReturnsNull()
    {
        var url = SourceUrlBuilder.BuildCommitUrl(RepositoryProvider.Github, "openshock", "firmware", "");
        await Assert.That(url).IsNull();
    }

    [Test]
    public async Task BuildRefUrl_TagRef_ReturnsReleasesTagUrl()
    {
        var url = SourceUrlBuilder.BuildRefUrl(RepositoryProvider.Github, "openshock", "firmware", "refs/tags/v1.5.1");
        await Assert.That(url).IsEqualTo("https://github.com/openshock/firmware/releases/tag/v1.5.1");
    }

    [Test]
    public async Task BuildRefUrl_BranchRef_ReturnsTreeUrl()
    {
        var url = SourceUrlBuilder.BuildRefUrl(RepositoryProvider.Github, "openshock", "firmware", "refs/heads/main");
        await Assert.That(url).IsEqualTo("https://github.com/openshock/firmware/tree/main");
    }

    [Test]
    public async Task BuildRefUrl_BareRef_TreatedAsBranch()
    {
        var url = SourceUrlBuilder.BuildRefUrl(RepositoryProvider.Github, "openshock", "firmware", "develop");
        await Assert.That(url).IsEqualTo("https://github.com/openshock/firmware/tree/develop");
    }

    [Test]
    public async Task BuildRefUrl_Null_ReturnsNull()
    {
        var url = SourceUrlBuilder.BuildRefUrl(RepositoryProvider.Github, "openshock", "firmware", null);
        await Assert.That(url).IsNull();
    }

    [Test]
    public async Task BuildRunUrl_Github_ReturnsActionsRunUrl()
    {
        var url = SourceUrlBuilder.BuildRunUrl(RepositoryProvider.Github, "openshock", "firmware", "12345678901");
        await Assert.That(url).IsEqualTo("https://github.com/openshock/firmware/actions/runs/12345678901");
    }

    [Test]
    public async Task BuildRunUrl_Empty_ReturnsNull()
    {
        var url = SourceUrlBuilder.BuildRunUrl(RepositoryProvider.Github, "openshock", "firmware", "");
        await Assert.That(url).IsNull();
    }
}
