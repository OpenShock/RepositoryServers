using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using OpenShock.RepositoryServer.Enums;
using OpenShock.RepositoryServer.Models.Firmware;
using OpenShock.RepositoryServer.RepoServerDb;

namespace OpenShock.RepositoryServer.Tests.Integration.Tests;

[NotInParallel("repo-server-integration")]
public class RepositoriesAdminControllerTests
{
    private const string BasePath = "/v2/firmware/admin/repositories";

    [ClassDataSource<WebApplicationFactory>(Shared = SharedType.PerTestSession)]
    public required WebApplicationFactory Factory { get; init; }

    [Before(Test)]
    public Task Setup() => Factory.ResetDatabaseAsync();

    [Test]
    public async Task Get_EmptyDatabase_ReturnsEmptyArray()
    {
        using var client = Factory.CreateAdminClient();
        var repos = await client.GetFromJsonAsync<List<RepositoryDto>>(BasePath);
        await Assert.That(repos).IsNotNull();
        await Assert.That(repos!).HasCount(0);
    }

    [Test]
    public async Task Get_WithSeededData_ReturnsRepositories()
    {
        Guid repoId;
        await using (var scope = Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RepoServerContext>();
            var repo = new SourceRepository
            {
                Id = Guid.NewGuid(),
                Provider = RepositoryProvider.Github,
                Owner = "openshock",
                Repo = "firmware"
            };
            db.Repositories.Add(repo);
            await db.SaveChangesAsync();
            repoId = repo.Id;
        }

        using var client = Factory.CreateAdminClient();
        var repos = await client.GetFromJsonAsync<List<RepositoryDto>>(BasePath);
        await Assert.That(repos).IsNotNull();
        await Assert.That(repos!).HasCount(1);
        await Assert.That(repos[0].Id).IsEqualTo(repoId);
        await Assert.That(repos[0].Provider).IsEqualTo("github");
        await Assert.That(repos[0].Owner).IsEqualTo("openshock");
        await Assert.That(repos[0].Repo).IsEqualTo("firmware");
    }

    [Test]
    public async Task Delete_Unreferenced_Returns204()
    {
        Guid repoId;
        await using (var scope = Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RepoServerContext>();
            var repo = new SourceRepository
            {
                Id = Guid.NewGuid(),
                Provider = RepositoryProvider.Github,
                Owner = "openshock",
                Repo = "desktop"
            };
            db.Repositories.Add(repo);
            await db.SaveChangesAsync();
            repoId = repo.Id;
        }

        using var client = Factory.CreateAdminClient();
        var response = await client.DeleteAsync($"{BasePath}/{repoId}");
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
    public async Task Delete_ReferencedByFirmwareVersion_Returns409()
    {
        Guid repoId;
        await using (var scope = Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RepoServerContext>();
            var repo = new SourceRepository
            {
                Id = Guid.NewGuid(),
                Provider = RepositoryProvider.Github,
                Owner = "openshock",
                Repo = "firmware"
            };
            var version = new FirmwareVersion
            {
                Version = "1.5.1",
                Channel = ReleaseChannel.Stable,
                ReleaseDate = DateTimeOffset.UtcNow,
                RepositoryId = repo.Id,
                CommitHash = "abc1234567890abcdef1234567890abcdef12345"
            };
            db.Repositories.Add(repo);
            db.FirmwareVersions.Add(version);
            await db.SaveChangesAsync();
            repoId = repo.Id;
        }

        using var client = Factory.CreateAdminClient();
        var response = await client.DeleteAsync($"{BasePath}/{repoId}");
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
    }

    [Test]
    public async Task Post_NotAllowed_ReturnsMethodNotAllowed()
    {
        using var client = Factory.CreateAdminClient();
        var response = await client.PostAsJsonAsync(BasePath, new { Provider = "github", Owner = "x", Repo = "y" });
        // Repositories are created automatically by the OIDC handler — manual creation
        // is not exposed, so POST must be rejected.
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.MethodNotAllowed);
    }
}
