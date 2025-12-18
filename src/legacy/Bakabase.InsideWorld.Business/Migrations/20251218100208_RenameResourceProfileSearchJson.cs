using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bakabase.InsideWorld.Business.Migrations
{
    /// <inheritdoc />
    public partial class RenameResourceProfileSearchJson : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SearchCriteriaJson",
                table: "ResourceProfiles");

            migrationBuilder.RenameColumn(
                name: "Filter",
                table: "BulkModifications",
                newName: "SearchJson");

            migrationBuilder.AddColumn<string>(
                name: "SearchJson",
                table: "ResourceProfiles",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SearchJson",
                table: "ResourceProfiles");

            migrationBuilder.RenameColumn(
                name: "SearchJson",
                table: "BulkModifications",
                newName: "Filter");

            migrationBuilder.AddColumn<string>(
                name: "SearchCriteriaJson",
                table: "ResourceProfiles",
                type: "TEXT",
                nullable: false,
                defaultValue: "");
        }
    }
}
