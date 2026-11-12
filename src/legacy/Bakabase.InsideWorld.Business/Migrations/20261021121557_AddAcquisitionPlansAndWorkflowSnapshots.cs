using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bakabase.InsideWorld.Business.Migrations
{
    /// <inheritdoc />
    public partial class AddAcquisitionPlansAndWorkflowSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PlanSnapshotJson",
                table: "WorkflowRuns",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "ResourceId",
                table: "AcquisitionTasks",
                type: "INTEGER",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "INTEGER");

            migrationBuilder.AlterColumn<int>(
                name: "RecipeDefinitionId",
                table: "AcquisitionTasks",
                type: "INTEGER",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "INTEGER");

            migrationBuilder.AddColumn<string>(
                name: "BuilderId",
                table: "AcquisitionTasks",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BuilderStateJson",
                table: "AcquisitionTasks",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Phase",
                table: "AcquisitionTasks",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PlanHistoryJson",
                table: "AcquisitionTasks",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PlanJson",
                table: "AcquisitionTasks",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PlanRevision",
                table: "AcquisitionTasks",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "SourceRefsJson",
                table: "AcquisitionTasks",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Title",
                table: "AcquisitionTasks",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PlanSnapshotJson",
                table: "WorkflowRuns");

            migrationBuilder.DropColumn(
                name: "BuilderId",
                table: "AcquisitionTasks");

            migrationBuilder.DropColumn(
                name: "BuilderStateJson",
                table: "AcquisitionTasks");

            migrationBuilder.DropColumn(
                name: "Phase",
                table: "AcquisitionTasks");

            migrationBuilder.DropColumn(
                name: "PlanHistoryJson",
                table: "AcquisitionTasks");

            migrationBuilder.DropColumn(
                name: "PlanJson",
                table: "AcquisitionTasks");

            migrationBuilder.DropColumn(
                name: "PlanRevision",
                table: "AcquisitionTasks");

            migrationBuilder.DropColumn(
                name: "SourceRefsJson",
                table: "AcquisitionTasks");

            migrationBuilder.DropColumn(
                name: "Title",
                table: "AcquisitionTasks");

            migrationBuilder.AlterColumn<int>(
                name: "ResourceId",
                table: "AcquisitionTasks",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "INTEGER",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "RecipeDefinitionId",
                table: "AcquisitionTasks",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "INTEGER",
                oldNullable: true);
        }
    }
}
