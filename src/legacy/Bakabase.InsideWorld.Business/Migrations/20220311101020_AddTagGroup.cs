using Microsoft.EntityFrameworkCore.Migrations;

namespace Bakabase.InsideWorld.Business.Migrations
{
    public partial class AddTagGroup : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "GroupId",
                table: "Tags",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "TagGroups",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TagGroups", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TagGroups_Name",
                table: "TagGroups",
                column: "Name",
                unique: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TagGroups");

            migrationBuilder.DropColumn(
                name: "GroupId",
                table: "Tags");
        }
    }
}
