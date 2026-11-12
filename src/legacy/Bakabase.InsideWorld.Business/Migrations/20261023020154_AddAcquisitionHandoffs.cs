using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bakabase.InsideWorld.Business.Migrations
{
    /// <inheritdoc />
    public partial class AddAcquisitionHandoffs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AcquisitionHandoffs",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    SourceModule = table.Column<string>(type: "TEXT", nullable: false),
                    SourceKind = table.Column<string>(type: "TEXT", nullable: false),
                    SourceId = table.Column<string>(type: "TEXT", nullable: false),
                    SourceRevision = table.Column<string>(type: "TEXT", nullable: false),
                    ProducerRunId = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    Code = table.Column<string>(type: "TEXT", nullable: true),
                    AcquisitionTaskId = table.Column<int>(type: "INTEGER", nullable: true),
                    OutputDirectory = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AcquisitionHandoffs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AcquisitionHandoffs_SourceModule_SourceKind_SourceId_SourceRevision",
                table: "AcquisitionHandoffs",
                columns: new[] { "SourceModule", "SourceKind", "SourceId", "SourceRevision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AcquisitionHandoffs_Status",
                table: "AcquisitionHandoffs",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AcquisitionHandoffs");
        }
    }
}
