using Microsoft.EntityFrameworkCore.Migrations;

namespace Bakabase.InsideWorld.Business.Migrations
{
    public partial class AddCustomPlayerAndPlayableFileSelector : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "ResolverType",
                table: "ResourceCategories",
                newName: "ComponentsJsonData");

            migrationBuilder.AddColumn<bool>(
                name: "IsSingleFile",
                table: "Resources",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "CustomPlayableFileSelectorOptionsList",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", nullable: true),
                    MaxFileCount = table.Column<int>(type: "INTEGER", nullable: false),
                    ExtensionsString = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomPlayableFileSelectorOptionsList", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CustomPlayerOptionsList",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", nullable: true),
                    Executable = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomPlayerOptionsList", x => x.Id);
                });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CustomPlayableFileSelectorOptionsList");

            migrationBuilder.DropTable(
                name: "CustomPlayerOptionsList");

            migrationBuilder.DropColumn(
                name: "IsSingleFile",
                table: "Resources");

            migrationBuilder.RenameColumn(
                name: "ComponentsJsonData",
                table: "ResourceCategories",
                newName: "ResolverType");
        }
    }
}
