using Microsoft.EntityFrameworkCore.Migrations;

namespace Bakabase.InsideWorld.Business.Migrations
{
    public partial class AddCategoryAndTagAndTagGroupOrder : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Order",
                table: "Tags",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Order",
                table: "TagGroups",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Order",
                table: "ResourceCategories",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Order",
                table: "Tags");

            migrationBuilder.DropColumn(
                name: "Order",
                table: "TagGroups");

            migrationBuilder.DropColumn(
                name: "Order",
                table: "ResourceCategories");
        }
    }
}
