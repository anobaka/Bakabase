using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bakabase.InsideWorld.Business.Migrations
{
    /// <inheritdoc />
    public partial class AddTargetsToPostParser : Migration
    {
        // NOTE: This migration was hand-edited to avoid a SQLite limitation —
        // `RenameColumn` on a table with multiple operations triggers an
        // EF Core "table rebuild" that emits `PRAGMA foreign_keys = 0;`,
        // which SQLite refuses to run inside a transaction (see issue #1108).
        // We replaced the two RenameColumn calls with DropColumn + AddColumn;
        // legacy data in `ParsedAt` and `Items` is intentionally discarded —
        // PostParserTasks is a transient task queue, not user-owned data.
        // The Designer snapshot already targets the new schema, so a future
        // `dotnet ef migrations add` will treat the model as in-sync.

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Error",
                table: "PostParserTasks");

            migrationBuilder.DropColumn(
                name: "ParsedAt",
                table: "PostParserTasks");

            migrationBuilder.DropColumn(
                name: "Items",
                table: "PostParserTasks");

            migrationBuilder.AddColumn<string>(
                name: "Targets",
                table: "PostParserTasks",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Results",
                table: "PostParserTasks",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Targets",
                table: "PostParserTasks");

            migrationBuilder.DropColumn(
                name: "Results",
                table: "PostParserTasks");

            migrationBuilder.AddColumn<string>(
                name: "Error",
                table: "PostParserTasks",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ParsedAt",
                table: "PostParserTasks",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Items",
                table: "PostParserTasks",
                type: "TEXT",
                nullable: true);
        }
    }
}
