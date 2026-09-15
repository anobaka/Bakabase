using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bakabase.InsideWorld.Business.Migrations
{
    /// <inheritdoc />
    public partial class AddCollections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CollectionResourceMappings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CollectionId = table.Column<int>(type: "INTEGER", nullable: false),
                    ResourceId = table.Column<int>(type: "INTEGER", nullable: false),
                    Origin = table.Column<int>(type: "INTEGER", nullable: false),
                    SubscriptionId = table.Column<int>(type: "INTEGER", nullable: true),
                    LastSeenAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    IsIgnored = table.Column<bool>(type: "INTEGER", nullable: false),
                    Order = table.Column<int>(type: "INTEGER", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CollectionResourceMappings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Collections",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: true),
                    Color = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    CoverPath = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true),
                    RuleSearchJson = table.Column<string>(type: "TEXT", nullable: true),
                    AutoAcquire = table.Column<bool>(type: "INTEGER", nullable: false),
                    AcquisitionSettingsJson = table.Column<string>(type: "TEXT", nullable: true),
                    Order = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Collections", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CollectionResourceMappings_CollectionId_ResourceId",
                table: "CollectionResourceMappings",
                columns: new[] { "CollectionId", "ResourceId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CollectionResourceMappings_ResourceId",
                table: "CollectionResourceMappings",
                column: "ResourceId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CollectionResourceMappings");

            migrationBuilder.DropTable(
                name: "Collections");
        }
    }
}
