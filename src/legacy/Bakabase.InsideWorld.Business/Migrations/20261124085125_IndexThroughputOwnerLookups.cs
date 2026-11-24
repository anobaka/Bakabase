using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bakabase.InsideWorld.Business.Migrations
{
    /// <inheritdoc />
    public partial class IndexThroughputOwnerLookups : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_ArtifactOperationBindings_OperationId",
                table: "ArtifactOperationBindings",
                column: "OperationId");

            migrationBuilder.CreateIndex(
                name: "IX_AcquisitionTasks_WorkflowRunId",
                table: "AcquisitionTasks",
                column: "WorkflowRunId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ArtifactOperationBindings_OperationId",
                table: "ArtifactOperationBindings");

            migrationBuilder.DropIndex(
                name: "IX_AcquisitionTasks_WorkflowRunId",
                table: "AcquisitionTasks");
        }
    }
}
