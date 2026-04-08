using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bakabase.InsideWorld.Business.Migrations
{
    /// <inheritdoc />
    public partial class AddTargetsToPostParser : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Error",
                table: "PostParserTasks");

            migrationBuilder.RenameColumn(
                name: "ParsedAt",
                table: "PostParserTasks",
                newName: "Targets");

            migrationBuilder.RenameColumn(
                name: "Items",
                table: "PostParserTasks",
                newName: "Results");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "Targets",
                table: "PostParserTasks",
                newName: "ParsedAt");

            migrationBuilder.RenameColumn(
                name: "Results",
                table: "PostParserTasks",
                newName: "Items");

            migrationBuilder.AddColumn<string>(
                name: "Error",
                table: "PostParserTasks",
                type: "TEXT",
                nullable: true);
        }
    }
}
