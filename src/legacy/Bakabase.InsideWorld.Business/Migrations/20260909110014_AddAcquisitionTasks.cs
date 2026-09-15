using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bakabase.InsideWorld.Business.Migrations
{
    /// <inheritdoc />
    public partial class AddAcquisitionTasks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsBuiltin",
                table: "WorkflowDefinitions",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "AcquisitionTasks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ResourceId = table.Column<int>(type: "INTEGER", nullable: false),
                    CollectionId = table.Column<int>(type: "INTEGER", nullable: true),
                    LeadKind = table.Column<int>(type: "INTEGER", nullable: false),
                    LeadValue = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    AcquisitionLeadId = table.Column<int>(type: "INTEGER", nullable: true),
                    RecipeDefinitionId = table.Column<int>(type: "INTEGER", nullable: false),
                    WorkflowRunId = table.Column<int>(type: "INTEGER", nullable: true),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    WaitReason = table.Column<int>(type: "INTEGER", nullable: true),
                    TargetDirectory = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true),
                    PurchaseRecordJson = table.Column<string>(type: "TEXT", nullable: true),
                    Error = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AcquisitionTasks", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AcquisitionTasks_ResourceId",
                table: "AcquisitionTasks",
                column: "ResourceId");

            migrationBuilder.CreateIndex(
                name: "IX_AcquisitionTasks_Status",
                table: "AcquisitionTasks",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AcquisitionTasks");

            migrationBuilder.DropColumn(
                name: "IsBuiltin",
                table: "WorkflowDefinitions");
        }
    }
}
