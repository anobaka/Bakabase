using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bakabase.InsideWorld.Business.Migrations
{
    /// <inheritdoc />
    public partial class IndependentBtDownloads : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "RequiresContentsDownload",
                table: "DownloadResults",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "BtDispatchError",
                table: "DownloadResultProcessing",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BtDownloadId",
                table: "DownloadResultProcessing",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RequiresContentsDownload",
                table: "DownloadResults");

            migrationBuilder.DropColumn(
                name: "BtDispatchError",
                table: "DownloadResultProcessing");

            migrationBuilder.DropColumn(
                name: "BtDownloadId",
                table: "DownloadResultProcessing");
        }
    }
}
