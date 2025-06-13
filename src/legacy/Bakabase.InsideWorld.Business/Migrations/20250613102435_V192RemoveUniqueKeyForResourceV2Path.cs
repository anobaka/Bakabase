using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bakabase.InsideWorld.Business.Migrations
{
    /// <inheritdoc />
    public partial class V192RemoveUniqueKeyForResourceV2Path : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ResourcesV2_Path",
                table: "ResourcesV2");

            migrationBuilder.CreateIndex(
                name: "IX_ResourcesV2_Path",
                table: "ResourcesV2",
                column: "Path");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ResourcesV2_Path",
                table: "ResourcesV2");

            migrationBuilder.CreateIndex(
                name: "IX_ResourcesV2_Path",
                table: "ResourcesV2",
                column: "Path",
                unique: true);
        }
    }
}
