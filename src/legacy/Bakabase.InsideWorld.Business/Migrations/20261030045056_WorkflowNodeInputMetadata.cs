using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bakabase.InsideWorld.Business.Migrations
{
    /// <inheritdoc />
    public partial class WorkflowNodeInputMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "InputsJson",
                table: "WorkflowNodeExecutions",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "InputsTruncated",
                table: "WorkflowNodeExecutions",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "InputsJson",
                table: "WorkflowNodeExecutions");

            migrationBuilder.DropColumn(
                name: "InputsTruncated",
                table: "WorkflowNodeExecutions");
        }
    }
}
