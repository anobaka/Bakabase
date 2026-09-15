using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bakabase.InsideWorld.Business.Migrations
{
    /// <inheritdoc />
    public partial class AddAcquisitionLeads : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AcquisitionLeads",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ResourceId = table.Column<int>(type: "INTEGER", nullable: false),
                    Kind = table.Column<int>(type: "INTEGER", nullable: false),
                    Value = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
                    Origin = table.Column<int>(type: "INTEGER", nullable: false),
                    Note = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    LastUsedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastResult = table.Column<int>(type: "INTEGER", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AcquisitionLeads", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AcquisitionLeads_Kind_Value",
                table: "AcquisitionLeads",
                columns: new[] { "Kind", "Value" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AcquisitionLeads_ResourceId",
                table: "AcquisitionLeads",
                column: "ResourceId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AcquisitionLeads");
        }
    }
}
