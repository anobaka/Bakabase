using Microsoft.EntityFrameworkCore.Migrations;

namespace Bakabase.InsideWorld.Business.Migrations
{
    public partial class AddResourceRawCover : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RawCover",
                table: "Resources",
                type: "TEXT",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RawCover",
                table: "Resources");
        }
    }
}
