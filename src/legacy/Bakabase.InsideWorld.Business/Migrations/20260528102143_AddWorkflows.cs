using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bakabase.InsideWorld.Business.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkflows : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WorkflowActivities",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    WorkflowDefinitionId = table.Column<int>(type: "INTEGER", nullable: false),
                    Order = table.Column<int>(type: "INTEGER", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", nullable: false),
                    ConfigJson = table.Column<string>(type: "TEXT", nullable: false),
                    OnItemError = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkflowActivities", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WorkflowDefinitions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    TriggerKind = table.Column<string>(type: "TEXT", nullable: false),
                    TriggerFilterJson = table.Column<string>(type: "TEXT", nullable: true),
                    Enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastRunAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastError = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkflowDefinitions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WorkflowRuns",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    WorkflowDefinitionId = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    PayloadJson = table.Column<string>(type: "TEXT", nullable: true),
                    PayloadSummary = table.Column<string>(type: "TEXT", nullable: true),
                    InputCount = table.Column<int>(type: "INTEGER", nullable: false),
                    OutputCount = table.Column<int>(type: "INTEGER", nullable: false),
                    FailedItemCount = table.Column<int>(type: "INTEGER", nullable: false),
                    ErrorMessage = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkflowRuns", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowActivities_WorkflowDefinitionId",
                table: "WorkflowActivities",
                column: "WorkflowDefinitionId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowActivities_WorkflowDefinitionId_Order",
                table: "WorkflowActivities",
                columns: new[] { "WorkflowDefinitionId", "Order" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowDefinitions_Enabled",
                table: "WorkflowDefinitions",
                column: "Enabled");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowDefinitions_TriggerKind",
                table: "WorkflowDefinitions",
                column: "TriggerKind");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowRuns_StartedAt",
                table: "WorkflowRuns",
                column: "StartedAt");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowRuns_Status",
                table: "WorkflowRuns",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowRuns_WorkflowDefinitionId",
                table: "WorkflowRuns",
                column: "WorkflowDefinitionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WorkflowActivities");

            migrationBuilder.DropTable(
                name: "WorkflowDefinitions");

            migrationBuilder.DropTable(
                name: "WorkflowRuns");
        }
    }
}
