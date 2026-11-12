using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bakabase.InsideWorld.Business.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkflowSourceHistoryAndAcquisitionContext : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PreparedContextJson",
                table: "AcquisitionTasks",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "WorkflowRunSourceDbModel",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    WorkflowRunId = table.Column<int>(type: "INTEGER", nullable: false),
                    Module = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    EntityKind = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    EntityId = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    Revision = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ResultKey = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkflowRunSourceDbModel", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkflowRunSourceDbModel_WorkflowRuns_WorkflowRunId",
                        column: x => x.WorkflowRunId,
                        principalTable: "WorkflowRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowRunSourceDbModel_Module_EntityKind_EntityId_Revision_ResultKey",
                table: "WorkflowRunSourceDbModel",
                columns: new[] { "Module", "EntityKind", "EntityId", "Revision", "ResultKey" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowRunSourceDbModel_WorkflowRunId_Module_EntityKind_EntityId_Revision_ResultKey",
                table: "WorkflowRunSourceDbModel",
                columns: new[] { "WorkflowRunId", "Module", "EntityKind", "EntityId", "Revision", "ResultKey" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WorkflowRunSourceDbModel");

            migrationBuilder.DropColumn(
                name: "PreparedContextJson",
                table: "AcquisitionTasks");
        }
    }
}
