using System;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Bakabase.InsideWorld.Business.Migrations
{
    public partial class Init : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Aliases",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    GroupId = table.Column<int>(type: "INTEGER", nullable: false),
                    IsPreferred = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Aliases", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AliasGroups",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AliasGroups", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FileChangeLogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    BatchId = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Old = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    New = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    ChangeDt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FileChangeLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Logs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    DateTime = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Level = table.Column<int>(type: "INTEGER", nullable: false),
                    Logger = table.Column<string>(type: "TEXT", nullable: true),
                    Event = table.Column<string>(type: "TEXT", nullable: true),
                    Message = table.Column<string>(type: "TEXT", nullable: true),
                    Read = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Logs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MediaLibraries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", nullable: true),
                    CategoryId = table.Column<int>(type: "INTEGER", nullable: false),
                    Root = table.Column<string>(type: "TEXT", nullable: false),
                    Order = table.Column<int>(type: "INTEGER", nullable: false),
                    SyncDt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ResourceCount = table.Column<int>(type: "INTEGER", nullable: false),
                    SyncInterval = table.Column<TimeSpan>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MediaLibraries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OrganizationResourceMappings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PublisherId = table.Column<int>(type: "INTEGER", nullable: false),
                    ParentPublisherId = table.Column<int>(type: "INTEGER", nullable: true),
                    ResourceId = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrganizationResourceMappings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OriginalResourceMappings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    OriginalId = table.Column<int>(type: "INTEGER", nullable: false),
                    ResourceId = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OriginalResourceMappings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Originals",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Originals", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Publishers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", nullable: true),
                    Rank = table.Column<int>(type: "INTEGER", nullable: false),
                    Favorite = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Publishers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PublisherTagMappings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PublisherId = table.Column<int>(type: "INTEGER", nullable: false),
                    TagId = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PublisherTagMappings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ResourceCategories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", nullable: true),
                    ResolverType = table.Column<string>(type: "TEXT", nullable: true),
                    Color = table.Column<string>(type: "TEXT", nullable: true),
                    CreateDt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ResourceCategories", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Resources",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", nullable: true),
                    Directory = table.Column<string>(type: "TEXT", nullable: true),
                    Cover = table.Column<string>(type: "TEXT", nullable: true),
                    StartFiles = table.Column<string>(type: "TEXT", nullable: true),
                    Language = table.Column<int>(type: "INTEGER", nullable: false),
                    Rank = table.Column<int>(type: "INTEGER", nullable: false),
                    ReleaseDt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreateDt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdateDt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    FileCreateDt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    FileModifyDt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Fullname = table.Column<string>(type: "TEXT", nullable: true),
                    MediaLibraryId = table.Column<int>(type: "INTEGER", nullable: false),
                    CategoryId = table.Column<int>(type: "INTEGER", nullable: false),
                    ResolverVersion = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Resources", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ResourceTagMappings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TagId = table.Column<int>(type: "INTEGER", nullable: false),
                    ResourceId = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ResourceTagMappings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Serials",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Serials", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SpecialTexts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Value1 = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Value2 = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    Type = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SpecialTexts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SubscriptionProgresses",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SubscriptionId = table.Column<int>(type: "INTEGER", nullable: false),
                    Type = table.Column<int>(type: "INTEGER", nullable: false),
                    LatestSyncDt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubscriptionProgresses", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SubscriptionRecords",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SubscriptionId = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: true),
                    Url = table.Column<string>(type: "TEXT", nullable: true),
                    DownloadUrl = table.Column<string>(type: "TEXT", nullable: true),
                    Read = table.Column<bool>(type: "INTEGER", nullable: false),
                    CoverUrl = table.Column<string>(type: "TEXT", nullable: true),
                    PublishDt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Type = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubscriptionRecords", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Subscriptions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Keyword = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Subscriptions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SystemProperties",
                columns: table => new
                {
                    Key = table.Column<string>(type: "TEXT", nullable: false),
                    Value = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SystemProperties", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "Tags",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tags", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Volumes",
                columns: table => new
                {
                    ResourceId = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SerialId = table.Column<int>(type: "INTEGER", nullable: false),
                    Index = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: true),
                    Title = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Volumes", x => x.ResourceId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Aliases_GroupId",
                table: "Aliases",
                column: "GroupId");

            migrationBuilder.CreateIndex(
                name: "IX_Aliases_IsPreferred",
                table: "Aliases",
                column: "IsPreferred");

            migrationBuilder.CreateIndex(
                name: "IX_Aliases_Name",
                table: "Aliases",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_FileChangeLogs_BatchId",
                table: "FileChangeLogs",
                column: "BatchId");

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationResourceMappings_PublisherId_ResourceId_ParentPublisherId",
                table: "OrganizationResourceMappings",
                columns: new[] { "PublisherId", "ResourceId", "ParentPublisherId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OriginalResourceMappings_OriginalId_ResourceId",
                table: "OriginalResourceMappings",
                columns: new[] { "OriginalId", "ResourceId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PublisherTagMappings_TagId_PublisherId",
                table: "PublisherTagMappings",
                columns: new[] { "TagId", "PublisherId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionRecords_SubscriptionId",
                table: "SubscriptionRecords",
                column: "SubscriptionId");

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionRecords_SubscriptionId_PublishDt",
                table: "SubscriptionRecords",
                columns: new[] { "SubscriptionId", "PublishDt" });

            migrationBuilder.CreateIndex(
                name: "IX_Subscriptions_Keyword",
                table: "Subscriptions",
                column: "Keyword",
                unique: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Aliases");

            migrationBuilder.DropTable(
                name: "AliasGroups");

            migrationBuilder.DropTable(
                name: "FileChangeLogs");

            migrationBuilder.DropTable(
                name: "Logs");

            migrationBuilder.DropTable(
                name: "MediaLibraries");

            migrationBuilder.DropTable(
                name: "OrganizationResourceMappings");

            migrationBuilder.DropTable(
                name: "OriginalResourceMappings");

            migrationBuilder.DropTable(
                name: "Originals");

            migrationBuilder.DropTable(
                name: "Publishers");

            migrationBuilder.DropTable(
                name: "PublisherTagMappings");

            migrationBuilder.DropTable(
                name: "ResourceCategories");

            migrationBuilder.DropTable(
                name: "Resources");

            migrationBuilder.DropTable(
                name: "ResourceTagMappings");

            migrationBuilder.DropTable(
                name: "Serials");

            migrationBuilder.DropTable(
                name: "SpecialTexts");

            migrationBuilder.DropTable(
                name: "SubscriptionProgresses");

            migrationBuilder.DropTable(
                name: "SubscriptionRecords");

            migrationBuilder.DropTable(
                name: "Subscriptions");

            migrationBuilder.DropTable(
                name: "SystemProperties");

            migrationBuilder.DropTable(
                name: "Tags");

            migrationBuilder.DropTable(
                name: "Volumes");
        }
    }
}
