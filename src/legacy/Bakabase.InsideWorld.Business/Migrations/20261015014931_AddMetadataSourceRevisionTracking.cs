using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bakabase.InsideWorld.Business.Migrations
{
    /// <inheritdoc />
    public partial class AddMetadataSourceRevisionTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MetadataSourceChanges",
                columns: table => new
                {
                    ResourceId = table.Column<int>(type: "INTEGER", nullable: false),
                    Revision = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MetadataSourceChanges", x => x.ResourceId);
                });

            migrationBuilder.CreateTable(
                name: "MetadataSourceClocks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false),
                    Generation = table.Column<string>(type: "TEXT", nullable: false),
                    Revision = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MetadataSourceClocks", x => x.Id);
                    table.CheckConstraint("CK_MetadataSourceClocks_Singleton", "Id = 1");
                });

            migrationBuilder.CreateIndex(
                name: "IX_MetadataSourceChanges_Revision",
                table: "MetadataSourceChanges",
                column: "Revision");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MetadataSourceChanges");

            migrationBuilder.DropTable(
                name: "MetadataSourceClocks");
        }
    }
}
