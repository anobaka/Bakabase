using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bakabase.InsideWorld.Business.Migrations
{
    public partial class AddDownloadTaskCompletedAt : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Historical completed rows remain null: the last status-update time also records
            // failures and stops, and cannot establish when a download actually succeeded.
            migrationBuilder.AddColumn<DateTime>(
                name: "CompletedAt",
                table: "DownloadTasks",
                type: "TEXT",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "CompletedAt", table: "DownloadTasks");
        }
    }
}
