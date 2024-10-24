using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bakabase.InsideWorld.Business.Migrations
{
    /// <inheritdoc />
    public partial class V190Beta10 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EnhancedAt",
                table: "EnhancementRecords");

            migrationBuilder.AddColumn<DateTime>(
                name: "ContextAppliedAt",
                table: "EnhancementRecords",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ContextCreatedAt",
                table: "EnhancementRecords",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Status",
                table: "EnhancementRecords",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ContextAppliedAt",
                table: "EnhancementRecords");

            migrationBuilder.DropColumn(
                name: "ContextCreatedAt",
                table: "EnhancementRecords");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "EnhancementRecords");

            migrationBuilder.AddColumn<DateTime>(
                name: "EnhancedAt",
                table: "EnhancementRecords",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));
        }
    }
}
