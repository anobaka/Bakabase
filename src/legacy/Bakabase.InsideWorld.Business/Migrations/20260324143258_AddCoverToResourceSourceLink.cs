using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bakabase.InsideWorld.Business.Migrations
{
    /// <inheritdoc />
    public partial class AddCoverToResourceSourceLink : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CoverUrls",
                table: "ResourceSourceLinks",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LocalCoverPaths",
                table: "ResourceSourceLinks",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SourceMetadataMappings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Source = table.Column<int>(type: "INTEGER", nullable: false),
                    MetadataField = table.Column<string>(type: "TEXT", nullable: false),
                    TargetPool = table.Column<int>(type: "INTEGER", nullable: false),
                    TargetPropertyId = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SourceMetadataMappings", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SourceMetadataMappings");

            migrationBuilder.DropColumn(
                name: "CoverUrls",
                table: "ResourceSourceLinks");

            migrationBuilder.DropColumn(
                name: "LocalCoverPaths",
                table: "ResourceSourceLinks");
        }
    }
}
