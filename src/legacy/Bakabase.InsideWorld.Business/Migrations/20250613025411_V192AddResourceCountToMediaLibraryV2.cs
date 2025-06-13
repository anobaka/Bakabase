using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bakabase.InsideWorld.Business.Migrations
{
    /// <inheritdoc />
    public partial class V192AddResourceCountToMediaLibraryV2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ResourceCount",
                table: "MediaLibrariesV2",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ResourceCount",
                table: "MediaLibrariesV2");
        }
    }
}
