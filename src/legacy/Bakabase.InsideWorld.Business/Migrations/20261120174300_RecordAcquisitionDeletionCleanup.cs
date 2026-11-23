using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bakabase.InsideWorld.Business.Migrations
{
    /// <inheritdoc />
    public partial class RecordAcquisitionDeletionCleanup : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DeletedCleanupReasons",
                table: "AcquisitionTasks",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeletedDeliveryCleanup",
                table: "AcquisitionTasks",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeletedIntermediateCleanup",
                table: "AcquisitionTasks",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_AcquisitionTasks_DeletedAt_Id",
                table: "AcquisitionTasks",
                columns: new[] { "DeletedAt", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AcquisitionTasks_DeletedAt_Id",
                table: "AcquisitionTasks");

            migrationBuilder.DropColumn(
                name: "DeletedCleanupReasons",
                table: "AcquisitionTasks");

            migrationBuilder.DropColumn(
                name: "DeletedDeliveryCleanup",
                table: "AcquisitionTasks");

            migrationBuilder.DropColumn(
                name: "DeletedIntermediateCleanup",
                table: "AcquisitionTasks");
        }
    }
}
