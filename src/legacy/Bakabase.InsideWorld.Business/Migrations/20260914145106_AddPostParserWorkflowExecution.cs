using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bakabase.InsideWorld.Business.Migrations
{
    /// <inheritdoc />
    public partial class AddPostParserWorkflowExecution : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "OutputItemsJson",
                table: "WorkflowRuns",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "OutputPreviewTruncated",
                table: "WorkflowRuns",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "Revision",
                table: "PostParserTasks",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "Text",
                table: "PostParserTasks",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "WorkflowDefinitionId",
                table: "PostParserTasks",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "WorkflowRunId",
                table: "PostParserTasks",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AccessCode",
                table: "AcquisitionLeads",
                type: "TEXT",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsResolved",
                table: "AcquisitionLeads",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Password",
                table: "AcquisitionLeads",
                type: "TEXT",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceReference",
                table: "AcquisitionLeads",
                type: "TEXT",
                maxLength: 2048,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "OutputItemsJson",
                table: "WorkflowRuns");

            migrationBuilder.DropColumn(
                name: "OutputPreviewTruncated",
                table: "WorkflowRuns");

            migrationBuilder.DropColumn(
                name: "Revision",
                table: "PostParserTasks");

            migrationBuilder.DropColumn(
                name: "Text",
                table: "PostParserTasks");

            migrationBuilder.DropColumn(
                name: "WorkflowDefinitionId",
                table: "PostParserTasks");

            migrationBuilder.DropColumn(
                name: "WorkflowRunId",
                table: "PostParserTasks");

            migrationBuilder.DropColumn(
                name: "AccessCode",
                table: "AcquisitionLeads");

            migrationBuilder.DropColumn(
                name: "IsResolved",
                table: "AcquisitionLeads");

            migrationBuilder.DropColumn(
                name: "Password",
                table: "AcquisitionLeads");

            migrationBuilder.DropColumn(
                name: "SourceReference",
                table: "AcquisitionLeads");
        }
    }
}
