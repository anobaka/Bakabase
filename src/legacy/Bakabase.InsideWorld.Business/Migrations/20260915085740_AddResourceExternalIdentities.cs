using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bakabase.InsideWorld.Business.Migrations
{
    /// <inheritdoc />
    public partial class AddResourceExternalIdentities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ResourceExternalIdentities",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ResourceId = table.Column<int>(type: "INTEGER", nullable: false),
                    ThirdPartyId = table.Column<int>(type: "INTEGER", nullable: false),
                    ExternalId = table.Column<string>(type: "TEXT", nullable: false),
                    CreateDt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CoverUrls = table.Column<string>(type: "TEXT", nullable: true),
                    LocalCoverPaths = table.Column<string>(type: "TEXT", nullable: true),
                    CoverDownloadFailedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    MetadataJson = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ResourceExternalIdentities", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ResourceExternalIdentities_ResourceId",
                table: "ResourceExternalIdentities",
                column: "ResourceId");

            migrationBuilder.CreateIndex(
                name: "IX_ResourceExternalIdentities_ResourceId_ThirdPartyId_ExternalId",
                table: "ResourceExternalIdentities",
                columns: new[] { "ResourceId", "ThirdPartyId", "ExternalId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ResourceExternalIdentities_ThirdPartyId_ExternalId",
                table: "ResourceExternalIdentities",
                columns: new[] { "ThirdPartyId", "ExternalId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ResourceExternalIdentities");
        }
    }
}
