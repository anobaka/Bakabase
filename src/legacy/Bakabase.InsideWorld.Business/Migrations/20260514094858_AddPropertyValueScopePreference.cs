using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bakabase.InsideWorld.Business.Migrations
{
    /// <inheritdoc />
    public partial class AddPropertyValueScopePreference : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PropertyValueScopePreferences",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ResourceId = table.Column<int>(type: "INTEGER", nullable: false),
                    PropertyPool = table.Column<int>(type: "INTEGER", nullable: false),
                    PropertyId = table.Column<int>(type: "INTEGER", nullable: false),
                    Priorities = table.Column<string>(type: "TEXT", nullable: true),
                    FallbackOnEmpty = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PropertyValueScopePreferences", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PropertyValueScopePreferences_ResourceId",
                table: "PropertyValueScopePreferences",
                column: "ResourceId");

            migrationBuilder.CreateIndex(
                name: "IX_PropertyValueScopePreferences_ResourceId_PropertyPool_PropertyId",
                table: "PropertyValueScopePreferences",
                columns: new[] { "ResourceId", "PropertyPool", "PropertyId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PropertyValueScopePreferences");
        }
    }
}
