using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenShock.Desktop.RepositoryServer.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "modules",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    description = table.Column<string>(type: "text", nullable: false),
                    source_url = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    icon_url = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("modules_pkey", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "versions",
                columns: table => new
                {
                    version = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    module = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    zip_url = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    hash_sha256 = table.Column<byte[]>(type: "bytea", nullable: false),
                    changelog_url = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    release_url = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("versions_pkey", x => new { x.version, x.module });
                    table.ForeignKey(
                        name: "fk_versions_module",
                        column: x => x.module,
                        principalTable: "modules",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_versions_module",
                table: "versions",
                column: "module");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "versions");

            migrationBuilder.DropTable(
                name: "modules");
        }
    }
}
