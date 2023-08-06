using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bakabase.InsideWorld.Business.Migrations
{
    public partial class V170 : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "HasChildren",
                table: "Resources",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "ParentId",
                table: "Resources",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "DownloadTasks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Key = table.Column<string>(type: "TEXT", nullable: true),
                    Name = table.Column<string>(type: "TEXT", nullable: true),
                    ThirdPartyId = table.Column<int>(type: "INTEGER", nullable: false),
                    Type = table.Column<int>(type: "INTEGER", nullable: false),
                    Progress = table.Column<decimal>(type: "TEXT", nullable: false),
                    DownloadStatusUpdateDt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Interval = table.Column<long>(type: "INTEGER", nullable: true),
                    StartPage = table.Column<int>(type: "INTEGER", nullable: true),
                    EndPage = table.Column<int>(type: "INTEGER", nullable: true),
                    Message = table.Column<string>(type: "TEXT", nullable: true),
                    Checkpoint = table.Column<string>(type: "TEXT", nullable: true),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    DownloadPath = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DownloadTasks", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DownloadTasks_Status",
                table: "DownloadTasks",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_DownloadTasks_ThirdPartyId",
                table: "DownloadTasks",
                column: "ThirdPartyId");

            migrationBuilder.CreateIndex(
                name: "IX_DownloadTasks_ThirdPartyId_Type",
                table: "DownloadTasks",
                columns: new[] { "ThirdPartyId", "Type" });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DownloadTasks");

            migrationBuilder.DropColumn(
                name: "HasChildren",
                table: "Resources");

            migrationBuilder.DropColumn(
                name: "ParentId",
                table: "Resources");
        }
    }
}
