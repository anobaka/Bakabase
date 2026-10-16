using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bakabase.InsideWorld.Business.Migrations
{
    /// <inheritdoc />
    public partial class AddResourceProcessingJobs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ResourceProcessingJobs",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    JobId = table.Column<string>(type: "TEXT", nullable: false),
                    Role = table.Column<string>(type: "TEXT", nullable: false),
                    RequesterId = table.Column<string>(type: "TEXT", nullable: false),
                    OwnerId = table.Column<string>(type: "TEXT", nullable: false),
                    OwnerEpoch = table.Column<string>(type: "TEXT", nullable: false),
                    ResourceId = table.Column<int>(type: "INTEGER", nullable: false),
                    ResourceRevision = table.Column<long>(type: "INTEGER", nullable: false),
                    RuleHash = table.Column<string>(type: "TEXT", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", nullable: false),
                    State = table.Column<string>(type: "TEXT", nullable: false),
                    InputJson = table.Column<string>(type: "TEXT", nullable: false),
                    ResultJson = table.Column<string>(type: "TEXT", nullable: true),
                    ErrorCode = table.Column<string>(type: "TEXT", nullable: true),
                    Error = table.Column<string>(type: "TEXT", nullable: true),
                    ProcessedCount = table.Column<long>(type: "INTEGER", nullable: false),
                    TotalCount = table.Column<long>(type: "INTEGER", nullable: true),
                    Phase = table.Column<string>(type: "TEXT", nullable: true),
                    Attempt = table.Column<int>(type: "INTEGER", nullable: false),
                    PauseRequested = table.Column<bool>(type: "INTEGER", nullable: false),
                    CancelRequested = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Version = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ResourceProcessingJobs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ResourceProcessingJobs_JobId",
                table: "ResourceProcessingJobs",
                column: "JobId");

            migrationBuilder.CreateIndex(
                name: "IX_ResourceProcessingJobs_OwnerId_OwnerEpoch_ResourceId",
                table: "ResourceProcessingJobs",
                columns: new[] { "OwnerId", "OwnerEpoch", "ResourceId" });

            migrationBuilder.CreateIndex(
                name: "IX_ResourceProcessingJobs_Role_State_UpdatedAtUtc",
                table: "ResourceProcessingJobs",
                columns: new[] { "Role", "State", "UpdatedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ResourceProcessingJobs");
        }
    }
}
