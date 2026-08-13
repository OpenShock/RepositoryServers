using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
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
///   * applies the real EF migrations (via <see cref="MigrationOpenShockContext"/>, the
///     context the migrations are attributed to) so the suite validates them
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
        var loggerFactory = scope.ServiceProvider.GetRequiredService<ILoggerFactory>();
        await using var migrationContext = new MigrationOpenShockContext(
            PostgreSql.Container.GetConnectionString(), debug: false, loggerFactory);
        await migrationContext.Database.MigrateAsync();
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
    /// Returns an <see cref="HttpClient"/> authenticated as the CI/CD principal of
    /// <paramref name="repositoryId"/>, which must be a registered repository.
    /// </summary>
    public HttpClient CreateCiCdClient(Guid repositoryId, string? commitHash = null, string? scopes = null)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.TryAddWithoutValidation(
            TestCiCdAuthHandler.RepositoryIdHeader, repositoryId.ToString());
        if (scopes is not null)
        {
            client.DefaultRequestHeaders.TryAddWithoutValidation(
                TestCiCdAuthHandler.ScopesHeader, scopes);
        }
        if (commitHash is not null)
        {
            client.DefaultRequestHeaders.TryAddWithoutValidation(
                TestCiCdAuthHandler.CommitHashHeader, commitHash);
        }
        return client;
    }

    /// <summary>
    /// True when a path exists in the local CDN storage backing this factory. Lets tests assert on
    /// what actually reached storage, rather than inferring it from response bodies.
    /// </summary>
    public bool StoredFileExists(string relativePath) =>
        File.Exists(Path.Combine(_cdnStoragePath, relativePath.Replace('/', Path.DirectorySeparatorChar)));

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
                discord_webhooks,
                versions,
                modules,
                repositories
            RESTART IDENTITY CASCADE;
            """, ct);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        // Program.cs reads and validates ApiConfig BEFORE builder.Build(). For minimal-hosting
        // apps, ConfigureAppConfiguration overlays are only applied at Build() time — too late.
        // UseSetting goes through host configuration, which the deferred host builder flattens
        // into command-line args for Program.Main, so Program's `.AddCommandLine(args)` picks
        // these up before the config is validated.
        var settings = new Dictionary<string, string?>
        {
            ["Db:Conn"] = PostgreSql.Container.GetConnectionString(),
            ["Db:SkipMigration"] = "true",
            ["Db:Debug"] = "false",

            ["AdminToken"] = TestAdminToken.Value,

            ["CiCd:Audience"] = "openshock-repository-server-test",

            ["Repo:CdnBaseUrl"] = "https://cdn-test.openshock.example/repo",

            ["Firmware:CdnBaseUrl"] = "https://cdn-test.openshock.example/firmware",
            ["Firmware:Storage:Type"] = "Local",
            ["Firmware:Storage:Local:BasePath"] = _cdnStoragePath,
            ["Firmware:StagedReleaseTtl"] = "01:00:00",
            ["Firmware:EditingReleaseTtl"] = "7.00:00:00",
        };
        foreach (var (key, value) in settings)
        {
            builder.UseSetting(key, value);
        }

        builder.ConfigureTestServices(services =>
        {
            // Repoint the CI/CD scheme at the test handler — see TestCiCdAuthHandler for what this
            // does and does not substitute. The scheme is already registered as JwtBearer by
            // Program.cs, and registering the same name twice fails host startup, so swap the handler
            // type on the existing registration rather than adding a second one.
            services.PostConfigure<AuthenticationOptions>(options =>
            {
                if (options.SchemeMap.TryGetValue(AuthSchemas.CiCdToken, out var scheme))
                {
                    scheme.HandlerType = typeof(TestCiCdAuthHandler);
                }
            });

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
