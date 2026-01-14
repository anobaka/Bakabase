using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bakabase.InsideWorld.Business.Migrations
{
    /// <inheritdoc />
    public partial class AddComparison : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ComparisonPlans",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    ResourceFilterId = table.Column<int>(type: "INTEGER", nullable: true),
                    Threshold = table.Column<double>(type: "REAL", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastRunAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ComparisonPlans", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ComparisonResultGroupMembers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    GroupId = table.Column<int>(type: "INTEGER", nullable: false),
                    ResourceId = table.Column<int>(type: "INTEGER", nullable: false),
                    IsSuggestedPrimary = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ComparisonResultGroupMembers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ComparisonResultGroups",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PlanId = table.Column<int>(type: "INTEGER", nullable: false),
                    MemberCount = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ComparisonResultGroups", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ComparisonRules",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PlanId = table.Column<int>(type: "INTEGER", nullable: false),
                    Order = table.Column<int>(type: "INTEGER", nullable: false),
                    PropertyPool = table.Column<int>(type: "INTEGER", nullable: false),
                    PropertyId = table.Column<int>(type: "INTEGER", nullable: false),
                    PropertyValueScope = table.Column<int>(type: "INTEGER", nullable: true),
                    Mode = table.Column<int>(type: "INTEGER", nullable: false),
                    Parameter = table.Column<string>(type: "TEXT", nullable: true),
                    Weight = table.Column<int>(type: "INTEGER", nullable: false),
                    IsVeto = table.Column<bool>(type: "INTEGER", nullable: false),
                    VetoThreshold = table.Column<double>(type: "REAL", nullable: false),
                    OneNullBehavior = table.Column<int>(type: "INTEGER", nullable: false),
                    BothNullBehavior = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ComparisonRules", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ComparisonPlans_CreatedAt",
                table: "ComparisonPlans",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_ComparisonPlans_Name",
                table: "ComparisonPlans",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_ComparisonResultGroupMembers_GroupId",
                table: "ComparisonResultGroupMembers",
                column: "GroupId");

            migrationBuilder.CreateIndex(
                name: "IX_ComparisonResultGroupMembers_GroupId_ResourceId",
                table: "ComparisonResultGroupMembers",
                columns: new[] { "GroupId", "ResourceId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ComparisonResultGroupMembers_ResourceId",
                table: "ComparisonResultGroupMembers",
                column: "ResourceId");

            migrationBuilder.CreateIndex(
                name: "IX_ComparisonResultGroups_MemberCount",
                table: "ComparisonResultGroups",
                column: "MemberCount");

            migrationBuilder.CreateIndex(
                name: "IX_ComparisonResultGroups_PlanId",
                table: "ComparisonResultGroups",
                column: "PlanId");

            migrationBuilder.CreateIndex(
                name: "IX_ComparisonResultGroups_PlanId_MemberCount",
                table: "ComparisonResultGroups",
                columns: new[] { "PlanId", "MemberCount" });

            migrationBuilder.CreateIndex(
                name: "IX_ComparisonRules_PlanId",
                table: "ComparisonRules",
                column: "PlanId");

            migrationBuilder.CreateIndex(
                name: "IX_ComparisonRules_PlanId_Order",
                table: "ComparisonRules",
                columns: new[] { "PlanId", "Order" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ComparisonPlans");

            migrationBuilder.DropTable(
                name: "ComparisonResultGroupMembers");

            migrationBuilder.DropTable(
                name: "ComparisonResultGroups");

            migrationBuilder.DropTable(
                name: "ComparisonRules");
        }
    }
}
