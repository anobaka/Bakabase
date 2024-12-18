using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bakabase.InsideWorld.Business.Migrations
{
    /// <inheritdoc />
    public partial class MergePinnedAndHasChildrenIntoTagsInResourceV2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "HasChildren",
                table: "ResourcesV2");

            migrationBuilder.DropColumn(
                name: "Pinned",
                table: "ResourcesV2");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "HasChildren",
                table: "ResourcesV2",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "Pinned",
                table: "ResourcesV2",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }
    }
}
