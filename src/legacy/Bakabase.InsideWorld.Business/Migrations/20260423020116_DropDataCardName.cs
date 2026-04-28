using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bakabase.InsideWorld.Business.Migrations
{
    /// <inheritdoc />
    public partial class DropDataCardName : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_DataCards_Name",
                table: "DataCards");

            migrationBuilder.DropColumn(
                name: "Name",
                table: "DataCards");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Name",
                table: "DataCards",
                type: "TEXT",
                maxLength: 512,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_DataCards_Name",
                table: "DataCards",
                column: "Name");
        }
    }
}
