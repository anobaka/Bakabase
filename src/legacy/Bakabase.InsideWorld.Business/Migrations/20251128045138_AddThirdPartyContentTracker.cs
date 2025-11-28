using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bakabase.InsideWorld.Business.Migrations
{
    /// <inheritdoc />
    public partial class AddThirdPartyContentTracker : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ThirdPartyContentTrackers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    DomainKey = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Filter = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    ContentId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ViewedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ThirdPartyContentTrackers", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ThirdPartyContentTrackers_DomainKey",
                table: "ThirdPartyContentTrackers",
                column: "DomainKey");

            migrationBuilder.CreateIndex(
                name: "IX_ThirdPartyContentTrackers_DomainKey_Filter_ContentId",
                table: "ThirdPartyContentTrackers",
                columns: new[] { "DomainKey", "Filter", "ContentId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ThirdPartyContentTrackers_ViewedAt",
                table: "ThirdPartyContentTrackers",
                column: "ViewedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ThirdPartyContentTrackers");
        }
    }
}
