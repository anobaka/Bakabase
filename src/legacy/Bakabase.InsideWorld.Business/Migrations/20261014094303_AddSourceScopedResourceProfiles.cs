using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bakabase.InsideWorld.Business.Migrations
{
    /// <inheritdoc />
    public partial class AddSourceScopedResourceProfiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "OpeningMode",
                table: "ResourceProfiles",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceDeviceId",
                table: "ResourceProfiles",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceLibraryEpoch",
                table: "ResourceProfiles",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "OpeningMode",
                table: "ResourceProfiles");

            migrationBuilder.DropColumn(
                name: "SourceDeviceId",
                table: "ResourceProfiles");

            migrationBuilder.DropColumn(
                name: "SourceLibraryEpoch",
                table: "ResourceProfiles");
        }
    }
}
