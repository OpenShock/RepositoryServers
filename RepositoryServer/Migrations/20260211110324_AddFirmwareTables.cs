using System;
using Microsoft.EntityFrameworkCore.Migrations;
using OpenShock.RepositoryServer.RepoServerDb;

#nullable disable

namespace OpenShock.RepositoryServer.Migrations
{
    /// <inheritdoc />
    public partial class AddFirmwareTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:Enum:firmware_artifact_type", "app,bootloader,merged,partitions,static_fs")
                .Annotation("Npgsql:Enum:firmware_artifact_type.firmware_artifact_type", "merged,app,bootloader,partitions,static_fs")
                .Annotation("Npgsql:Enum:firmware_channel", "beta,develop,stable")
                .Annotation("Npgsql:Enum:firmware_channel.firmware_channel", "stable,beta,develop")
                .Annotation("Npgsql:Enum:firmware_chip_architecture", "risc_v,xtensa")
                .Annotation("Npgsql:Enum:firmware_chip_architecture.firmware_chip_architecture", "xtensa,risc_v")
                .Annotation("Npgsql:Enum:firmware_release_note_type", "breaking,info,section,warning")
                .Annotation("Npgsql:Enum:firmware_release_note_type.firmware_release_note_type", "warning,info,breaking,section");

            migrationBuilder.CreateTable(
                name: "firmware_chips",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    architecture = table.Column<FirmwareChipArchitecture>(type: "firmware_chip_architecture", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("firmware_chips_pkey", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "firmware_versions",
                columns: table => new
                {
                    version = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    channel = table.Column<FirmwareChannel>(type: "firmware_channel", nullable: false),
                    release_date = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    commit_hash = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    release_url = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("firmware_versions_pkey", x => x.version);
                });

            migrationBuilder.CreateTable(
                name: "firmware_boards",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    chip_id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    discontinued = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("firmware_boards_pkey", x => x.id);
                    table.ForeignKey(
                        name: "fk_firmware_boards_chip",
                        column: x => x.chip_id,
                        principalTable: "firmware_chips",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "firmware_release_notes",
                columns: table => new
                {
                    version = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    index = table.Column<int>(type: "integer", nullable: false),
                    type = table.Column<FirmwareReleaseNoteType>(type: "firmware_release_note_type", nullable: false),
                    title = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    content = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("firmware_release_notes_pkey", x => new { x.version, x.index });
                    table.ForeignKey(
                        name: "fk_firmware_release_notes_version",
                        column: x => x.version,
                        principalTable: "firmware_versions",
                        principalColumn: "version",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "firmware_artifacts",
                columns: table => new
                {
                    version = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    board_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    artifact_type = table.Column<FirmwareArtifactType>(type: "firmware_artifact_type", nullable: false),
                    hash_sha256 = table.Column<byte[]>(type: "bytea", nullable: false),
                    file_size = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("firmware_artifacts_pkey", x => new { x.version, x.board_id, x.artifact_type });
                    table.ForeignKey(
                        name: "fk_firmware_artifacts_board",
                        column: x => x.board_id,
                        principalTable: "firmware_boards",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_firmware_artifacts_version",
                        column: x => x.version,
                        principalTable: "firmware_versions",
                        principalColumn: "version",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_firmware_artifacts_board_id",
                table: "firmware_artifacts",
                column: "board_id");

            migrationBuilder.CreateIndex(
                name: "IX_firmware_boards_chip_id",
                table: "firmware_boards",
                column: "chip_id");

            migrationBuilder.CreateIndex(
                name: "ix_firmware_versions_channel",
                table: "firmware_versions",
                column: "channel");

            migrationBuilder.CreateIndex(
                name: "ix_firmware_versions_release_date",
                table: "firmware_versions",
                column: "release_date");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "firmware_artifacts");

            migrationBuilder.DropTable(
                name: "firmware_release_notes");

            migrationBuilder.DropTable(
                name: "firmware_boards");

            migrationBuilder.DropTable(
                name: "firmware_versions");

            migrationBuilder.DropTable(
                name: "firmware_chips");

            migrationBuilder.AlterDatabase()
                .OldAnnotation("Npgsql:Enum:firmware_artifact_type", "app,bootloader,merged,partitions,static_fs")
                .OldAnnotation("Npgsql:Enum:firmware_artifact_type.firmware_artifact_type", "merged,app,bootloader,partitions,static_fs")
                .OldAnnotation("Npgsql:Enum:firmware_channel", "beta,develop,stable")
                .OldAnnotation("Npgsql:Enum:firmware_channel.firmware_channel", "stable,beta,develop")
                .OldAnnotation("Npgsql:Enum:firmware_chip_architecture", "risc_v,xtensa")
                .OldAnnotation("Npgsql:Enum:firmware_chip_architecture.firmware_chip_architecture", "xtensa,risc_v")
                .OldAnnotation("Npgsql:Enum:firmware_release_note_type", "breaking,info,section,warning")
                .OldAnnotation("Npgsql:Enum:firmware_release_note_type.firmware_release_note_type", "warning,info,breaking,section");
        }
    }
}
