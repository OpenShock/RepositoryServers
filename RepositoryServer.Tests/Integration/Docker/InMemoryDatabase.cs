using Testcontainers.PostgreSql;
using TUnit.Core.Interfaces;

namespace OpenShock.RepositoryServer.Tests.Integration.Docker;

/// <summary>
/// Per-test-session Postgres container. Mirrors the <c>InMemoryDatabase</c> pattern from
/// the OpenShock API integration tests. Lazy-starts on first access, disposed at end of
/// test session.
/// </summary>
public sealed class InMemoryDatabase : IAsyncInitializer, IAsyncDisposable
{
    private PostgreSqlContainer? _container;

    public PostgreSqlContainer Container
    {
        get
        {
            _container ??= new PostgreSqlBuilder()
                .WithImage("postgres:17-alpine")
                .WithName($"repo-server-tests-pg-{Guid.NewGuid():N}")
                .WithDatabase("repo_server_tests")
                .WithUsername("repo_server")
                .WithPassword("repo_server_test_password")
                .Build();

            return _container;
        }
    }

    public Task InitializeAsync() => Container.StartAsync();

    public async ValueTask DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }
}
