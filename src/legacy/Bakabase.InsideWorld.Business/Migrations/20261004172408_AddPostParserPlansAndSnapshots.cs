using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bakabase.InsideWorld.Business.Migrations
{
    /// <inheritdoc />
    public partial class AddPostParserPlansAndSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AvailabilityJson",
                table: "PostParserTasks",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContentSnapshotJson",
                table: "PostParserTasks",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ParsingMessage",
                table: "PostParserTasks",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ParsingState",
                table: "PostParserTasks",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExtractionPlanJson",
                table: "AcquisitionLeads",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AvailabilityJson",
                table: "PostParserTasks");

            migrationBuilder.DropColumn(
                name: "ContentSnapshotJson",
                table: "PostParserTasks");

            migrationBuilder.DropColumn(
                name: "ParsingMessage",
                table: "PostParserTasks");

            migrationBuilder.DropColumn(
                name: "ParsingState",
                table: "PostParserTasks");

            migrationBuilder.DropColumn(
                name: "ExtractionPlanJson",
                table: "AcquisitionLeads");
        }
    }
}
