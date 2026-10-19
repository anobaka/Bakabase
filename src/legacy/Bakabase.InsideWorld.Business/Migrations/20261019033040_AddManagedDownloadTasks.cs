using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bakabase.InsideWorld.Business.Migrations
{
    /// <inheritdoc />
    public partial class AddManagedDownloadTasks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ManagedDownloadTasks",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    Category = table.Column<string>(type: "TEXT", nullable: false),
                    Provider = table.Column<string>(type: "TEXT", nullable: false),
                    ProviderTaskId = table.Column<string>(type: "TEXT", nullable: false),
                    ParentId = table.Column<string>(type: "TEXT", nullable: true),
                    SiteTaskId = table.Column<int>(type: "INTEGER", nullable: true),
                    OriginKind = table.Column<string>(type: "TEXT", nullable: false),
                    OriginId = table.Column<string>(type: "TEXT", nullable: true),
                    WorkflowRunId = table.Column<int>(type: "INTEGER", nullable: true),
                    WorkflowDefinitionId = table.Column<int>(type: "INTEGER", nullable: true),
                    DownloadResultId = table.Column<int>(type: "INTEGER", nullable: true),
                    UserStopped = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ManagedDownloadTasks", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ManagedDownloadTasks_ParentId",
                table: "ManagedDownloadTasks",
                column: "ParentId");

            migrationBuilder.CreateIndex(
                name: "IX_ManagedDownloadTasks_Provider_ProviderTaskId",
                table: "ManagedDownloadTasks",
                columns: new[] { "Provider", "ProviderTaskId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ManagedDownloadTasks_WorkflowRunId",
                table: "ManagedDownloadTasks",
                column: "WorkflowRunId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ManagedDownloadTasks");
        }
    }
}
