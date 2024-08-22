using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bakabase.InsideWorld.Business.Migrations
{
    public partial class V171 : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "EnhancerAssemblyQualifiedTypeName",
                table: "EnhancementRecords",
                newName: "EnhancerName");

            migrationBuilder.AddColumn<string>(
                name: "EnhancerDescriptorId",
                table: "EnhancementRecords",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ResourceRawFullName",
                table: "EnhancementRecords",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CategoryComponents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CategoryId = table.Column<int>(type: "INTEGER", nullable: false),
                    ComponentKey = table.Column<string>(type: "TEXT", nullable: false),
                    ComponentType = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CategoryComponents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ComponentOptions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ComponentType = table.Column<int>(type: "INTEGER", nullable: false),
                    ComponentAssemblyQualifiedTypeName = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: true),
                    Json = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ComponentOptions", x => x.Id);
                });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CategoryComponents");

            migrationBuilder.DropTable(
                name: "ComponentOptions");

            migrationBuilder.DropColumn(
                name: "EnhancerDescriptorId",
                table: "EnhancementRecords");

            migrationBuilder.DropColumn(
                name: "ResourceRawFullName",
                table: "EnhancementRecords");

            migrationBuilder.RenameColumn(
                name: "EnhancerName",
                table: "EnhancementRecords",
                newName: "EnhancerAssemblyQualifiedTypeName");
        }
    }
}
