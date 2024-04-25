using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bakabase.InsideWorld.Business.Migrations
{
    public partial class V190Beta1Enhancement : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EnhancementRecords");

            migrationBuilder.CreateTable(
                name: "CategoryEnhancerOptions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CategoryId = table.Column<int>(type: "INTEGER", nullable: false),
                    EnhancerId = table.Column<int>(type: "INTEGER", nullable: false),
                    TargetPropertyIdMap = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CategoryEnhancerOptions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Enhancements",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ResourceId = table.Column<int>(type: "INTEGER", nullable: false),
                    EnhancerId = table.Column<int>(type: "INTEGER", nullable: false),
                    Target = table.Column<int>(type: "INTEGER", nullable: false),
                    ValueType = table.Column<int>(type: "INTEGER", nullable: false),
                    Value = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Enhancements", x => x.Id);
                });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CategoryEnhancerOptions");

            migrationBuilder.DropTable(
                name: "Enhancements");

            migrationBuilder.CreateTable(
                name: "EnhancementRecords",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CreateDt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Enhancement = table.Column<string>(type: "TEXT", nullable: true),
                    EnhancerDescriptorId = table.Column<string>(type: "TEXT", nullable: true),
                    EnhancerName = table.Column<string>(type: "TEXT", nullable: true),
                    Message = table.Column<string>(type: "TEXT", nullable: true),
                    ResourceId = table.Column<int>(type: "INTEGER", nullable: false),
                    ResourceRawFullName = table.Column<string>(type: "TEXT", nullable: true),
                    RuleId = table.Column<string>(type: "TEXT", nullable: true),
                    Success = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EnhancementRecords", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EnhancementRecords_ResourceId",
                table: "EnhancementRecords",
                column: "ResourceId");
        }
    }
}
