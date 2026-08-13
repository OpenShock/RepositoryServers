using Microsoft.EntityFrameworkCore.Design;

namespace OpenShock.RepositoryServer.RepoServerDb;

public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<MigrationOpenShockContext>
{
    public MigrationOpenShockContext CreateDbContext(string[] args)
    {
        return new MigrationOpenShockContext();
    }
}
