using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bakabase.InsideWorld.Business.Migrations
{
    /// <inheritdoc />
    public partial class AddReusableResourceScopes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ResourceScopeId",
                table: "ResourceProfiles",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ResourceScopes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    SourceDeviceId = table.Column<string>(type: "TEXT", nullable: true),
                    SourceLibraryEpoch = table.Column<string>(type: "TEXT", nullable: true),
                    SearchJson = table.Column<string>(type: "TEXT", nullable: true),
                    Revision = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ResourceScopes", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ResourceProfiles_ResourceScopeId",
                table: "ResourceProfiles",
                column: "ResourceScopeId");

            migrationBuilder.AddForeignKey(
                name: "FK_ResourceProfiles_ResourceScopes_ResourceScopeId",
                table: "ResourceProfiles",
                column: "ResourceScopeId",
                principalTable: "ResourceScopes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ResourceProfiles_ResourceScopes_ResourceScopeId",
                table: "ResourceProfiles");

            migrationBuilder.DropTable(
                name: "ResourceScopes");

            migrationBuilder.DropIndex(
                name: "IX_ResourceProfiles_ResourceScopeId",
                table: "ResourceProfiles");

            migrationBuilder.DropColumn(
                name: "ResourceScopeId",
                table: "ResourceProfiles");
        }
    }
}
