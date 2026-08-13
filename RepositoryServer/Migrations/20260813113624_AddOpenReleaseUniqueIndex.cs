using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenShock.RepositoryServer.Migrations
{
    /// <summary>
    /// Enforces "at most one open release per version" in the database.
    /// </summary>
    /// <remarks>
    /// <c>InitRelease</c> guards this with a read-then-insert, which two concurrent jobs for the same
    /// tag both pass. Both then stage artifacts into the same deterministic CDN keys and race each
    /// other's uploads, producing a permanent hash mismatch for every OTA client with no error
    /// surfaced anywhere. A partial unique index closes the window; the controller's existing check
    /// stays as the path that returns a clean 409 in the common case.
    ///
    /// Partial indexes cannot be expressed in the EF model, so this is raw SQL — see also
    /// AddCaseInsensitiveNameIndexes.
    /// </remarks>
    public partial class AddOpenReleaseUniqueIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                CREATE UNIQUE INDEX ix_firmware_releases_open_version
                    ON firmware_releases (version)
                    WHERE status IN ('staging', 'editing');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_firmware_releases_open_version;");
        }
    }
}
