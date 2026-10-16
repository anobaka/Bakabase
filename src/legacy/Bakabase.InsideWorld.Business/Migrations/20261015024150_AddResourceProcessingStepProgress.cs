using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bakabase.InsideWorld.Business.Migrations
{
    /// <inheritdoc />
    public partial class AddResourceProcessingStepProgress : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CurrentStepPhase",
                table: "ResourceProcessingJobs",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "CurrentStepProcessedCount",
                table: "ResourceProcessingJobs",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "CurrentStepTotalCount",
                table: "ResourceProcessingJobs",
                type: "INTEGER",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CurrentStepPhase",
                table: "ResourceProcessingJobs");

            migrationBuilder.DropColumn(
                name: "CurrentStepProcessedCount",
                table: "ResourceProcessingJobs");

            migrationBuilder.DropColumn(
                name: "CurrentStepTotalCount",
                table: "ResourceProcessingJobs");
        }
    }
}
