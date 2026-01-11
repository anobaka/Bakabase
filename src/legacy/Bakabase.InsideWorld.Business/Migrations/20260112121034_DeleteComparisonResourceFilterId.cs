using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bakabase.InsideWorld.Business.Migrations
{
    /// <inheritdoc />
    public partial class DeleteComparisonResourceFilterId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ResourceFilterId",
                table: "ComparisonPlans");

            migrationBuilder.AddColumn<string>(
                name: "SearchJson",
                table: "ComparisonPlans",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SearchJson",
                table: "ComparisonPlans");

            migrationBuilder.AddColumn<int>(
                name: "ResourceFilterId",
                table: "ComparisonPlans",
                type: "INTEGER",
                nullable: true);
        }
    }
}
