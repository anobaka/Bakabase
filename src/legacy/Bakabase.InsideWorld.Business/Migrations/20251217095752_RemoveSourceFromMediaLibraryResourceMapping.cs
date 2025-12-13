using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bakabase.InsideWorld.Business.Migrations
{
    /// <inheritdoc />
    public partial class RemoveSourceFromMediaLibraryResourceMapping : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Source",
                table: "MediaLibraryResourceMappings");

            migrationBuilder.DropColumn(
                name: "SourceRuleId",
                table: "MediaLibraryResourceMappings");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Source",
                table: "MediaLibraryResourceMappings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "SourceRuleId",
                table: "MediaLibraryResourceMappings",
                type: "INTEGER",
                nullable: true);
        }
    }
}
