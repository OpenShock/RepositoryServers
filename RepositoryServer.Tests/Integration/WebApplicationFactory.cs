using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenShock.RepositoryServer.RepoServerDb;
using OpenShock.RepositoryServer.Services;
using OpenShock.RepositoryServer.Tests.Integration.Docker;
using TUnit.Core.Interfaces;

namespace OpenShock.RepositoryServer.Tests.Integration;

/// <summary>
/// Custom <see cref="Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory{TEntryPoint}"/>
/// wired up to a Testcontainers Postgres instance. The harness:
///   * overlays in-memory configuration on top of whatever Program.cs loads from disk
///     (overrides db connection, admin token, local storage path, and forces skip-migration)
///   * removes the <see cref="StagedReleaseCleanupService"/> hosted service so it doesn't
///     race the schema creation inside <see cref="InitializeAsync"/>
///   * calls <see cref="DatabaseFacade.EnsureCreatedAsync"/> to build the schema from the
///     current EF model — no real migrations are run because the project doesn't ship
///     firmware migrations yet
/// </summary>
public sealed class WebApplicationFactory
    : Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program>, IAsyncInitializer
{
    [ClassDataSource<InMemoryDatabase>(Shared = SharedType.PerTestSession)]
    public required InMemoryDatabase PostgreSql { get; init; }

    private readonly string _cdnStoragePath = Path.Combine(
        Path.GetTempPath(), $"repo-server-tests-cdn-{Guid.NewGuid():N}");

    public async Task InitializeAsync()
    {
        _ = Server; // force the host to build

        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<RepoServerContext>();
        await db.Database.EnsureCreatedAsync();
    }

    /// <summary>
    /// Returns an <see cref="HttpClient"/> with a pre-populated <c>Authorization</c>
    /// header matching the admin token baked into the test configuration.
    /// </summary>
    public HttpClient CreateAdminClient()
    {
        var client = CreateClient();
        // AdminTokenAuthentication compares the raw Authorization header to the configured
        // admin token — TryAddWithoutValidation bypasses HttpHeaders' scheme+token parser.
        client.DefaultRequestHeaders.TryAddWithoutValidation(
            TestAdminToken.HeaderName, TestAdminToken.Value);
        return client;
    }

    /// <summary>
    /// Wipes all mutable test data between tests without tearing down the container.
    /// Firmware tables first (FK order), then repositories, then catalog.
    /// </summary>
    public async Task ResetDatabaseAsync(CancellationToken ct = default)
    {
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<RepoServerContext>();

        await db.Database.ExecuteSqlRawAsync(
            """
            TRUNCATE TABLE
                firmware_staged_release_notes,
                firmware_staged_artifacts,
                firmware_releases,
                firmware_release_notes,
                firmware_artifacts,
                firmware_versions,
                firmware_board_usb_devices,
                firmware_chip_usb_devices,
                firmware_boards,
                firmware_chips,
                firmware_advisories,
                usb_serial_filters,
                usb_devices,
                repositories
            RESTART IDENTITY CASCADE;
            """, ct);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((_, cfg) =>
        {
            cfg.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Db:Conn"] = PostgreSql.Container.GetConnectionString(),
                ["Db:SkipMigration"] = "true",
                ["Db:Debug"] = "false",

                ["AdminToken"] = TestAdminToken.Value,

                ["Repo:CdnBaseUrl"] = "https://cdn-test.openshock.example/repo",

                ["Firmware:CdnBaseUrl"] = "https://cdn-test.openshock.example/firmware",
                ["Firmware:CiCd:Audience"] = "openshock-repository-server-test",
                ["Firmware:Storage:Type"] = "Local",
                ["Firmware:Storage:Local:BasePath"] = _cdnStoragePath,
                ["Firmware:StagedReleaseTtl"] = "01:00:00",
                ["Firmware:EditingReleaseTtl"] = "7.00:00:00",
            });
        });

        builder.ConfigureTestServices(services =>
        {
            // The cleanup hosted service fires on startup and queries the DB. In tests
            // we build schema AFTER the host starts, so suppress it to avoid noisy
            // failure logs. Individual tests exercise its logic directly if needed.
            var hostedDescriptor = services.FirstOrDefault(
                d => d.ImplementationType == typeof(StagedReleaseCleanupService));
            if (hostedDescriptor is not null)
            {
                services.Remove(hostedDescriptor);
            }
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing)
        {
            try
            {
                if (Directory.Exists(_cdnStoragePath))
                {
                    Directory.Delete(_cdnStoragePath, recursive: true);
                }
            }
            catch
            {
                // best effort — tests shouldn't fail on cleanup
            }
        }
    }
}
