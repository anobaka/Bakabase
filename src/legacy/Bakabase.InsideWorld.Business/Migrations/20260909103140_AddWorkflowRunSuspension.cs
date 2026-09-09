using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bakabase.InsideWorld.Business.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkflowRunSuspension : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CurrentItemJson",
                table: "WorkflowRuns",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CurrentStepIndex",
                table: "WorkflowRuns",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PendingSignalJson",
                table: "WorkflowRuns",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WaitPromptJson",
                table: "WorkflowRuns",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WaitReason",
                table: "WorkflowRuns",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "WaitingSince",
                table: "WorkflowRuns",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CurrentItemJson",
                table: "WorkflowRuns");

            migrationBuilder.DropColumn(
                name: "CurrentStepIndex",
                table: "WorkflowRuns");

            migrationBuilder.DropColumn(
                name: "PendingSignalJson",
                table: "WorkflowRuns");

            migrationBuilder.DropColumn(
                name: "WaitPromptJson",
                table: "WorkflowRuns");

            migrationBuilder.DropColumn(
                name: "WaitReason",
                table: "WorkflowRuns");

            migrationBuilder.DropColumn(
                name: "WaitingSince",
                table: "WorkflowRuns");
        }
    }
}
