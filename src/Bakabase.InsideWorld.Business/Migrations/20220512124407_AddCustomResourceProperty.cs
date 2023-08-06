using System;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Bakabase.InsideWorld.Business.Migrations
{
    public partial class AddCustomResourceProperty : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameTable(name: "Serials", newName: "Series");

            migrationBuilder.RenameColumn(
                name: "Rank",
                table: "Resources",
                newName: "Rate");

            migrationBuilder.AddColumn<string>(
                name: "Introduction",
                table: "Resources",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CommandTemplate",
                table: "CustomPlayerOptionsList",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CustomResourceProperties",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ResourceId = table.Column<int>(type: "INTEGER", nullable: false),
                    Key = table.Column<string>(type: "TEXT", nullable: true),
                    Index = table.Column<int>(type: "INTEGER", nullable: true),
                    Value = table.Column<string>(type: "TEXT", nullable: true),
                    ValueType = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table => { table.PrimaryKey("PK_CustomResourceProperties", x => x.Id); });

            migrationBuilder.CreateTable(
                name: "EnhancementRecords",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ResourceId = table.Column<int>(type: "INTEGER", nullable: false),
                    EnhancerAssemblyQualifiedTypeName = table.Column<string>(type: "TEXT", nullable: true),
                    RuleId = table.Column<string>(type: "TEXT", nullable: true),
                    Success = table.Column<bool>(type: "INTEGER", nullable: false),
                    Enhancement = table.Column<string>(type: "TEXT", nullable: true),
                    Message = table.Column<string>(type: "TEXT", nullable: true),
                    CreateDt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table => { table.PrimaryKey("PK_EnhancementRecords", x => x.Id); });

            migrationBuilder.AddColumn<string>(
                name: "EnhancementOptionsJson",
                table: "ResourceCategories",
                type: "TEXT",
                nullable: true);

            migrationBuilder.DropColumn(
                name: "Fullname",
                table: "Resources");

            migrationBuilder.DropColumn(
                name: "ResolverVersion",
                table: "Resources");

            migrationBuilder.AddColumn<bool>(
                name: "GenerateNfo",
                table: "ResourceCategories",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
            migrationBuilder.RenameColumn(
                name: "Root",
                table: "MediaLibraries",
                newName: "PathConfigurationsJson");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CustomResourceProperties");

            migrationBuilder.DropTable(
                name: "EnhancementRecords");

            migrationBuilder.DropColumn(
                name: "Introduction",
                table: "Resources");

            migrationBuilder.DropColumn(
                name: "CommandTemplate",
                table: "CustomPlayerOptionsList");

            migrationBuilder.RenameTable(name: "Series", newName: "Serials");

            migrationBuilder.RenameColumn(
                name: "Rate",
                table: "Resources",
                newName: "Rank");

            migrationBuilder.DropColumn(
                name: "EnhancementOptionsJson",
                table: "ResourceCategories");

            migrationBuilder.DropColumn(
                name: "GenerateNfo",
                table: "ResourceCategories");

            migrationBuilder.AddColumn<string>(
                name: "Fullname",
                table: "Resources",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ResolverVersion",
                table: "Resources",
                type: "TEXT",
                nullable: true);
            migrationBuilder.RenameColumn(
                name: "PathConfigurationsJson",
                table: "MediaLibraries",
                newName: "Root");
        }
    }
}