using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bakabase.InsideWorld.Business.Migrations
{
    /// <inheritdoc />
    public partial class RemoveSourceAndSourceKeyFromResource : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ResourcesV2_Source",
                table: "ResourcesV2");

            migrationBuilder.DropIndex(
                name: "IX_ResourcesV2_Source_SourceKey",
                table: "ResourcesV2");

            migrationBuilder.DropColumn(
                name: "Source",
                table: "ResourcesV2");

            migrationBuilder.DropColumn(
                name: "SourceKey",
                table: "ResourcesV2");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Source",
                table: "ResourcesV2",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "SourceKey",
                table: "ResourcesV2",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_ResourcesV2_Source",
                table: "ResourcesV2",
                column: "Source");

            migrationBuilder.CreateIndex(
                name: "IX_ResourcesV2_Source_SourceKey",
                table: "ResourcesV2",
                columns: new[] { "Source", "SourceKey" });
        }
    }
}
