using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bakabase.InsideWorld.Business.Migrations
{
    public partial class AddInitialIndexes : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Volumes_Name",
                table: "Volumes",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_Volumes_ResourceId",
                table: "Volumes",
                column: "ResourceId");

            migrationBuilder.CreateIndex(
                name: "IX_Volumes_SerialId",
                table: "Volumes",
                column: "SerialId");

            migrationBuilder.CreateIndex(
                name: "IX_Volumes_Title",
                table: "Volumes",
                column: "Title");

            migrationBuilder.CreateIndex(
                name: "IX_Tags_Name",
                table: "Tags",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_Series_Name",
                table: "Series",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_ResourceTagMappings_TagId_ResourceId",
                table: "ResourceTagMappings",
                columns: new[] { "TagId", "ResourceId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Resources_CategoryId",
                table: "Resources",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_Resources_CreateDt",
                table: "Resources",
                column: "CreateDt");

            migrationBuilder.CreateIndex(
                name: "IX_Resources_FileCreateDt",
                table: "Resources",
                column: "FileCreateDt");

            migrationBuilder.CreateIndex(
                name: "IX_Resources_FileModifyDt",
                table: "Resources",
                column: "FileModifyDt");

            migrationBuilder.CreateIndex(
                name: "IX_Resources_Language",
                table: "Resources",
                column: "Language");

            migrationBuilder.CreateIndex(
                name: "IX_Resources_Name",
                table: "Resources",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_Resources_Rate",
                table: "Resources",
                column: "Rate");

            migrationBuilder.CreateIndex(
                name: "IX_Resources_RawName",
                table: "Resources",
                column: "RawName");

            migrationBuilder.CreateIndex(
                name: "IX_Resources_UpdateDt",
                table: "Resources",
                column: "UpdateDt");

            migrationBuilder.CreateIndex(
                name: "IX_Publishers_Name",
                table: "Publishers",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_Originals_Name",
                table: "Originals",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_MediaLibraries_CategoryId",
                table: "MediaLibraries",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_MediaLibraries_Name",
                table: "MediaLibraries",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_EnhancementRecords_ResourceId",
                table: "EnhancementRecords",
                column: "ResourceId");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Volumes_Name",
                table: "Volumes");

            migrationBuilder.DropIndex(
                name: "IX_Volumes_ResourceId",
                table: "Volumes");

            migrationBuilder.DropIndex(
                name: "IX_Volumes_SerialId",
                table: "Volumes");

            migrationBuilder.DropIndex(
                name: "IX_Volumes_Title",
                table: "Volumes");

            migrationBuilder.DropIndex(
                name: "IX_Tags_Name",
                table: "Tags");

            migrationBuilder.DropIndex(
                name: "IX_Series_Name",
                table: "Series");

            migrationBuilder.DropIndex(
                name: "IX_ResourceTagMappings_TagId_ResourceId",
                table: "ResourceTagMappings");

            migrationBuilder.DropIndex(
                name: "IX_Resources_CategoryId",
                table: "Resources");

            migrationBuilder.DropIndex(
                name: "IX_Resources_CreateDt",
                table: "Resources");

            migrationBuilder.DropIndex(
                name: "IX_Resources_FileCreateDt",
                table: "Resources");

            migrationBuilder.DropIndex(
                name: "IX_Resources_FileModifyDt",
                table: "Resources");

            migrationBuilder.DropIndex(
                name: "IX_Resources_Language",
                table: "Resources");

            migrationBuilder.DropIndex(
                name: "IX_Resources_Name",
                table: "Resources");

            migrationBuilder.DropIndex(
                name: "IX_Resources_Rate",
                table: "Resources");

            migrationBuilder.DropIndex(
                name: "IX_Resources_RawName",
                table: "Resources");

            migrationBuilder.DropIndex(
                name: "IX_Resources_UpdateDt",
                table: "Resources");

            migrationBuilder.DropIndex(
                name: "IX_Publishers_Name",
                table: "Publishers");

            migrationBuilder.DropIndex(
                name: "IX_Originals_Name",
                table: "Originals");

            migrationBuilder.DropIndex(
                name: "IX_MediaLibraries_CategoryId",
                table: "MediaLibraries");

            migrationBuilder.DropIndex(
                name: "IX_MediaLibraries_Name",
                table: "MediaLibraries");

            migrationBuilder.DropIndex(
                name: "IX_EnhancementRecords_ResourceId",
                table: "EnhancementRecords");
        }
    }
}
