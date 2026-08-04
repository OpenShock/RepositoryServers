using System;
using Microsoft.EntityFrameworkCore.Migrations;
using OpenShock.RepositoryServer.Enums;

#nullable disable

namespace OpenShock.RepositoryServer.Migrations
{
    /// <inheritdoc />
    public partial class UnifiedRepositoryServer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:Enum:advisory_severity", "critical,info,warning")
                .Annotation("Npgsql:Enum:firmware_artifact_type", "app,bootloader,merged,partitions,static_fs")
                .Annotation("Npgsql:Enum:firmware_chip_architecture", "risc_v,xtensa")
                .Annotation("Npgsql:Enum:firmware_release_note_type", "breaking,info,section,warning")
                .Annotation("Npgsql:Enum:release_channel", "beta,develop,stable")
                .Annotation("Npgsql:Enum:release_status", "aborted,archived,editing,published,staging")
                .Annotation("Npgsql:Enum:repository_provider", "github");

            migrationBuilder.CreateTable(
                name: "firmware_advisories",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    severity = table.Column<AdvisorySeverity>(type: "advisory_severity", nullable: false),
                    title = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    content = table.Column<string>(type: "text", nullable: false),
                    affected_versions = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    url = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("firmware_advisories_pkey", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "firmware_chips",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    architecture = table.Column<FirmwareChipArchitecture>(type: "firmware_chip_architecture", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("firmware_chips_pkey", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "repositories",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider = table.Column<RepositoryProvider>(type: "repository_provider", nullable: false),
                    owner = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    repo = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("repositories_pkey", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "usb_devices",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    vid = table.Column<int>(type: "integer", nullable: false),
                    pid = table.Column<int>(type: "integer", nullable: false),
                    name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("usb_devices_pkey", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "usb_serial_filters",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    vid = table.Column<int>(type: "integer", nullable: false),
                    pid = table.Column<int>(type: "integer", nullable: true),
                    description = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("usb_serial_filters_pkey", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "firmware_boards",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    chip_id = table.Column<Guid>(type: "uuid", nullable: false),
                    discontinued = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    required_artifact_types = table.Column<FirmwareArtifactType[]>(type: "firmware_artifact_type[]", nullable: false)
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
                name: "firmware_releases",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    version = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    channel = table.Column<ReleaseChannel>(type: "release_channel", nullable: false),
                    repository_id = table.Column<Guid>(type: "uuid", nullable: false),
                    commit_hash = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    @ref = table.Column<string>(name: "ref", type: "character varying(256)", maxLength: 256, nullable: true),
                    run_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    release_date = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<ReleaseStatus>(type: "release_status", nullable: false),
                    declared_boards = table.Column<Guid[]>(type: "uuid[]", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("firmware_releases_pkey", x => x.id);
                    table.ForeignKey(
                        name: "fk_firmware_releases_repository",
                        column: x => x.repository_id,
                        principalTable: "repositories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "firmware_versions",
                columns: table => new
                {
                    version = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    channel = table.Column<ReleaseChannel>(type: "release_channel", nullable: false),
                    release_date = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    repository_id = table.Column<Guid>(type: "uuid", nullable: false),
                    commit_hash = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    @ref = table.Column<string>(name: "ref", type: "character varying(256)", maxLength: 256, nullable: true),
                    run_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("firmware_versions_pkey", x => x.version);
                    table.ForeignKey(
                        name: "fk_firmware_versions_repository",
                        column: x => x.repository_id,
                        principalTable: "repositories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "firmware_chip_usb_devices",
                columns: table => new
                {
                    chip_id = table.Column<Guid>(type: "uuid", nullable: false),
                    usb_device_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("firmware_chip_usb_devices_pkey", x => new { x.chip_id, x.usb_device_id });
                    table.ForeignKey(
                        name: "fk_firmware_chip_usb_devices_chip",
                        column: x => x.chip_id,
                        principalTable: "firmware_chips",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_firmware_chip_usb_devices_usb_device",
                        column: x => x.usb_device_id,
                        principalTable: "usb_devices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "firmware_board_usb_devices",
                columns: table => new
                {
                    board_id = table.Column<Guid>(type: "uuid", nullable: false),
                    usb_device_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("firmware_board_usb_devices_pkey", x => new { x.board_id, x.usb_device_id });
                    table.ForeignKey(
                        name: "fk_firmware_board_usb_devices_board",
                        column: x => x.board_id,
                        principalTable: "firmware_boards",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_firmware_board_usb_devices_usb_device",
                        column: x => x.usb_device_id,
                        principalTable: "usb_devices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "firmware_staged_artifacts",
                columns: table => new
                {
                    release_id = table.Column<Guid>(type: "uuid", nullable: false),
                    board_id = table.Column<Guid>(type: "uuid", nullable: false),
                    artifact_type = table.Column<FirmwareArtifactType>(type: "firmware_artifact_type", nullable: false),
                    hash_sha256 = table.Column<byte[]>(type: "bytea", nullable: false),
                    file_size = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("firmware_staged_artifacts_pkey", x => new { x.release_id, x.board_id, x.artifact_type });
                    table.ForeignKey(
                        name: "fk_firmware_staged_artifacts_release",
                        column: x => x.release_id,
                        principalTable: "firmware_releases",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "firmware_staged_release_notes",
                columns: table => new
                {
                    release_id = table.Column<Guid>(type: "uuid", nullable: false),
                    index = table.Column<int>(type: "integer", nullable: false),
                    type = table.Column<ReleaseNoteSectionType>(type: "firmware_release_note_type", nullable: false),
                    title = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    content = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("firmware_staged_release_notes_pkey", x => new { x.release_id, x.index });
                    table.ForeignKey(
                        name: "fk_firmware_staged_release_notes_release",
                        column: x => x.release_id,
                        principalTable: "firmware_releases",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "firmware_artifacts",
                columns: table => new
                {
                    version = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    board_id = table.Column<Guid>(type: "uuid", nullable: false),
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

            migrationBuilder.CreateTable(
                name: "firmware_release_notes",
                columns: table => new
                {
                    version = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    index = table.Column<int>(type: "integer", nullable: false),
                    type = table.Column<ReleaseNoteSectionType>(type: "firmware_release_note_type", nullable: false),
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

            migrationBuilder.CreateIndex(
                name: "IX_firmware_artifacts_board_id",
                table: "firmware_artifacts",
                column: "board_id");

            migrationBuilder.CreateIndex(
                name: "IX_firmware_board_usb_devices_usb_device_id",
                table: "firmware_board_usb_devices",
                column: "usb_device_id");

            migrationBuilder.CreateIndex(
                name: "IX_firmware_boards_chip_id",
                table: "firmware_boards",
                column: "chip_id");

            migrationBuilder.CreateIndex(
                name: "ix_firmware_boards_name",
                table: "firmware_boards",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_firmware_chip_usb_devices_usb_device_id",
                table: "firmware_chip_usb_devices",
                column: "usb_device_id");

            migrationBuilder.CreateIndex(
                name: "ix_firmware_chips_name",
                table: "firmware_chips",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_firmware_releases_repository_id",
                table: "firmware_releases",
                column: "repository_id");

            migrationBuilder.CreateIndex(
                name: "ix_firmware_releases_version_status",
                table: "firmware_releases",
                columns: new[] { "version", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_firmware_versions_channel",
                table: "firmware_versions",
                column: "channel");

            migrationBuilder.CreateIndex(
                name: "ix_firmware_versions_release_date",
                table: "firmware_versions",
                column: "release_date");

            migrationBuilder.CreateIndex(
                name: "IX_firmware_versions_repository_id",
                table: "firmware_versions",
                column: "repository_id");

            migrationBuilder.CreateIndex(
                name: "ix_repositories_provider_owner_repo",
                table: "repositories",
                columns: new[] { "provider", "owner", "repo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_usb_devices_vid_pid",
                table: "usb_devices",
                columns: new[] { "vid", "pid" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_usb_serial_filters_vid_pid",
                table: "usb_serial_filters",
                columns: new[] { "vid", "pid" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "firmware_advisories");

            migrationBuilder.DropTable(
                name: "firmware_artifacts");

            migrationBuilder.DropTable(
                name: "firmware_board_usb_devices");

            migrationBuilder.DropTable(
                name: "firmware_chip_usb_devices");

            migrationBuilder.DropTable(
                name: "firmware_release_notes");

            migrationBuilder.DropTable(
                name: "firmware_staged_artifacts");

            migrationBuilder.DropTable(
                name: "firmware_staged_release_notes");

            migrationBuilder.DropTable(
                name: "usb_serial_filters");

            migrationBuilder.DropTable(
                name: "firmware_boards");

            migrationBuilder.DropTable(
                name: "usb_devices");

            migrationBuilder.DropTable(
                name: "firmware_versions");

            migrationBuilder.DropTable(
                name: "firmware_releases");

            migrationBuilder.DropTable(
                name: "firmware_chips");

            migrationBuilder.DropTable(
                name: "repositories");

            migrationBuilder.AlterDatabase()
                .OldAnnotation("Npgsql:Enum:advisory_severity", "critical,info,warning")
                .OldAnnotation("Npgsql:Enum:firmware_artifact_type", "app,bootloader,merged,partitions,static_fs")
                .OldAnnotation("Npgsql:Enum:firmware_chip_architecture", "risc_v,xtensa")
                .OldAnnotation("Npgsql:Enum:firmware_release_note_type", "breaking,info,section,warning")
                .OldAnnotation("Npgsql:Enum:release_channel", "beta,develop,stable")
                .OldAnnotation("Npgsql:Enum:release_status", "aborted,archived,editing,published,staging")
                .OldAnnotation("Npgsql:Enum:repository_provider", "github");
        }
    }
}
