using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bakabase.InsideWorld.Business.Migrations
{
    /// <inheritdoc />
    public partial class V190AddReservedPropertySupportForEnhancer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BuiltinPropertyValues");

            migrationBuilder.DropColumn(
                name: "CustomPropertyValueId",
                table: "Enhancements");

            migrationBuilder.AddColumn<int>(
                name: "PropertyId",
                table: "Enhancements",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PropertyType",
                table: "Enhancements",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ReservedPropertyValues",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ResourceId = table.Column<int>(type: "INTEGER", nullable: false),
                    Scope = table.Column<int>(type: "INTEGER", nullable: false),
                    Rating = table.Column<decimal>(type: "TEXT", nullable: true),
                    Introduction = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReservedPropertyValues", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ReservedPropertyValues_ResourceId_Scope",
                table: "ReservedPropertyValues",
                columns: new[] { "ResourceId", "Scope" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ReservedPropertyValues");

            migrationBuilder.DropColumn(
                name: "PropertyId",
                table: "Enhancements");

            migrationBuilder.DropColumn(
                name: "PropertyType",
                table: "Enhancements");

            migrationBuilder.AddColumn<int>(
                name: "CustomPropertyValueId",
                table: "Enhancements",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "BuiltinPropertyValues",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Introduction = table.Column<string>(type: "TEXT", nullable: true),
                    Rating = table.Column<decimal>(type: "TEXT", nullable: true),
                    ResourceId = table.Column<int>(type: "INTEGER", nullable: false),
                    Scope = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BuiltinPropertyValues", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BuiltinPropertyValues_ResourceId_Scope",
                table: "BuiltinPropertyValues",
                columns: new[] { "ResourceId", "Scope" },
                unique: true);
        }
    }
}
