using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bakabase.InsideWorld.Business.Migrations
{
    /// <inheritdoc />
    public partial class RefactorScopePreferencePerScopeFallback : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FallbackOnEmpty",
                table: "PropertyValueScopePreferences");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "FallbackOnEmpty",
                table: "PropertyValueScopePreferences",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }
    }
}
