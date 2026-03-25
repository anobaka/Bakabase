using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bakabase.InsideWorld.Business.Migrations
{
    /// <inheritdoc />
    public partial class AddDeleteResourceToBulkModification : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "DeleteFiles",
                table: "BulkModifications",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "DeleteResources",
                table: "BulkModifications",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DeleteFiles",
                table: "BulkModifications");

            migrationBuilder.DropColumn(
                name: "DeleteResources",
                table: "BulkModifications");
        }
    }
}
