using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenShock.RepositoryServer.Migrations
{
    /// <inheritdoc />
    public partial class AddModuleOwningRepository : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "repository_id",
                table: "modules",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_modules_repository_id",
                table: "modules",
                column: "repository_id");

            migrationBuilder.AddForeignKey(
                name: "fk_modules_repository",
                table: "modules",
                column: "repository_id",
                principalTable: "repositories",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_modules_repository",
                table: "modules");

            migrationBuilder.DropIndex(
                name: "IX_modules_repository_id",
                table: "modules");

            migrationBuilder.DropColumn(
                name: "repository_id",
                table: "modules");
        }
    }
}
