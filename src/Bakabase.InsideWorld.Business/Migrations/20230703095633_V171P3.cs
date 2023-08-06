using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bakabase.InsideWorld.Business.Migrations
{
    public partial class V171P3 : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Passwords",
                columns: table => new
                {
                    Text = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    UsedTimes = table.Column<int>(type: "INTEGER", nullable: false),
                    LastUsedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Passwords", x => x.Text);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Passwords_LastUsedAt",
                table: "Passwords",
                column: "LastUsedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Passwords_UsedTimes",
                table: "Passwords",
                column: "UsedTimes");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Passwords");
        }
    }
}
