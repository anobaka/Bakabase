using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bakabase.InsideWorld.Business.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkflowDescriptionsAndNotes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Description",
                table: "WorkflowDefinitions",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DescriptionKey",
                table: "WorkflowDefinitions",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Notes",
                table: "WorkflowActivities",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Description",
                table: "WorkflowDefinitions");

            migrationBuilder.DropColumn(
                name: "DescriptionKey",
                table: "WorkflowDefinitions");

            migrationBuilder.DropColumn(
                name: "Notes",
                table: "WorkflowActivities");
        }
    }
}
