using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bakabase.InsideWorld.Business.Migrations;

public partial class AddDownloadTaskFiles : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "DownloadTaskFiles",
            columns: table => new
            {
                DownloadTaskId = table.Column<int>(type: "INTEGER", nullable: false),
                Path = table.Column<string>(type: "TEXT", nullable: false),
                Size = table.Column<long>(type: "INTEGER", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_DownloadTaskFiles", x => new {x.DownloadTaskId, x.Path});
                table.ForeignKey(
                    name: "FK_DownloadTaskFiles_DownloadTasks_DownloadTaskId",
                    column: x => x.DownloadTaskId,
                    principalTable: "DownloadTasks",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "DownloadTaskFiles");
    }
}
