using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenShock.RepositoryServer.Migrations
{
    /// <summary>
    /// Replaces the case-sensitive unique indexes on board and chip names with unique indexes on
    /// <c>lower(name)</c>.
    /// </summary>
    /// <remarks>
    /// Board and chip names are resolved case-insensitively on the public endpoints, but uniqueness was
    /// enforced case-sensitively. That let <c>ESP32-Core</c> and <c>esp32-core</c> coexist as separate
    /// boards, with resolution silently picking one of them — the other could never receive an upload,
    /// and a hub compiled with its spelling would be served the other board's firmware, potentially for
    /// a different chip. The functional index makes that state unrepresentable, and lets the
    /// <c>lower(name) = ...</c> lookups use an index instead of a sequential scan.
    ///
    /// This migration fails loudly if such duplicates already exist. That is deliberate: they must be
    /// merged by hand, since picking a winner automatically would silently orphan a board's artifacts.
    /// </remarks>
    public partial class AddCaseInsensitiveNameIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_firmware_chips_name",
                table: "firmware_chips");

            migrationBuilder.DropIndex(
                name: "ix_firmware_boards_name",
                table: "firmware_boards");

            // Retained for ordering and exact-name lookups; uniqueness now comes from the functional
            // indexes below.
            migrationBuilder.CreateIndex(
                name: "ix_firmware_chips_name",
                table: "firmware_chips",
                column: "name");

            migrationBuilder.CreateIndex(
                name: "ix_firmware_boards_name",
                table: "firmware_boards",
                column: "name");

            migrationBuilder.Sql(
                """
                CREATE UNIQUE INDEX ix_firmware_chips_name_lower
                    ON firmware_chips (lower(name));
                """);

            migrationBuilder.Sql(
                """
                CREATE UNIQUE INDEX ix_firmware_boards_name_lower
                    ON firmware_boards (lower(name));
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_firmware_boards_name_lower;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_firmware_chips_name_lower;");

            migrationBuilder.DropIndex(
                name: "ix_firmware_chips_name",
                table: "firmware_chips");

            migrationBuilder.DropIndex(
                name: "ix_firmware_boards_name",
                table: "firmware_boards");

            migrationBuilder.CreateIndex(
                name: "ix_firmware_chips_name",
                table: "firmware_chips",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_firmware_boards_name",
                table: "firmware_boards",
                column: "name",
                unique: true);
        }
    }
}
