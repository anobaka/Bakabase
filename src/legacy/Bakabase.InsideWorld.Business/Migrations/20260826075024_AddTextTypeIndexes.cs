using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bakabase.InsideWorld.Business.Migrations
{
    /// <inheritdoc />
    public partial class AddTextTypeIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_TextTypes_Name",
                table: "TextTypes",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_TextTypes_WellKnown",
                table: "TextTypes",
                column: "WellKnown");

            migrationBuilder.CreateIndex(
                name: "IX_TextEntries_TypeId",
                table: "TextEntries",
                column: "TypeId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TextTypes_Name",
                table: "TextTypes");

            migrationBuilder.DropIndex(
                name: "IX_TextTypes_WellKnown",
                table: "TextTypes");

            migrationBuilder.DropIndex(
                name: "IX_TextEntries_TypeId",
                table: "TextEntries");
        }
    }
}
