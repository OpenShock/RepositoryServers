using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
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
        await Assert.That(repos!).Count().IsEqualTo(0);
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
        await Assert.That(repos!).Count().IsEqualTo(1);
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
    public async Task Put_RegistersRepository_AsThePublishAllowlistEntry()
    {
        using var client = Factory.CreateAdminClient();

        var response = await client.PutAsJsonAsync(BasePath, new UpsertRepositoryRequest
        {
            Provider = "github",
            Owner = "openshock",
            Repo = "firmware"
        });

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Created);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        await Assert.That(body.GetProperty("owner").GetString()).IsEqualTo("openshock");
        await Assert.That(body.GetProperty("repo").GetString()).IsEqualTo("firmware");
        await Assert.That(Guid.Parse(body.GetProperty("id").GetString()!)).IsNotEqualTo(Guid.Empty);
    }

    [Test]
    public async Task Put_IsIdempotent_SoRerunningOnboardingIsHarmless()
    {
        using var client = Factory.CreateAdminClient();

        var first = await client.PutAsJsonAsync(BasePath, new UpsertRepositoryRequest
        {
            Provider = "github", Owner = "openshock", Repo = "firmware"
        });
        var firstId = (await first.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString();

        var second = await client.PutAsJsonAsync(BasePath, new UpsertRepositoryRequest
        {
            Provider = "github", Owner = "openshock", Repo = "firmware"
        });

        await Assert.That(second.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var secondId = (await second.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString();
        await Assert.That(secondId).IsEqualTo(firstId);
    }

    [Test]
    public async Task Put_UnknownProvider_Returns400()
    {
        using var client = Factory.CreateAdminClient();

        var response = await client.PutAsJsonAsync(BasePath, new UpsertRepositoryRequest
        {
            Provider = "bitbucket", Owner = "openshock", Repo = "firmware"
        });

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task Put_WithoutAdminToken_IsRejected()
    {
        // Onboarding is the authorization decision for publishing, so it must not be reachable
        // without the admin token.
        using var client = Factory.CreateClient();

        var response = await client.PutAsJsonAsync(BasePath, new UpsertRepositoryRequest
        {
            Provider = "github", Owner = "attacker", Repo = "evil"
        });

        await Assert.That(response.IsSuccessStatusCode).IsFalse();
    }
}
