using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bakabase.InsideWorld.Business.Migrations
{
    /// <inheritdoc />
    public partial class AddAiIntegration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AiFeatureConfigs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Feature = table.Column<int>(type: "INTEGER", nullable: false),
                    UseDefault = table.Column<bool>(type: "INTEGER", nullable: false),
                    ProviderConfigId = table.Column<int>(type: "INTEGER", nullable: true),
                    ModelId = table.Column<string>(type: "TEXT", nullable: true),
                    Temperature = table.Column<float>(type: "REAL", nullable: true),
                    MaxTokens = table.Column<int>(type: "INTEGER", nullable: true),
                    TopP = table.Column<float>(type: "REAL", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiFeatureConfigs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LlmCallCacheEntries",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CacheKey = table.Column<string>(type: "TEXT", nullable: false),
                    ResponseJson = table.Column<string>(type: "TEXT", nullable: false),
                    ProviderConfigId = table.Column<int>(type: "INTEGER", nullable: false),
                    ModelId = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    HitCount = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LlmCallCacheEntries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LlmProviderConfigs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ProviderType = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Endpoint = table.Column<string>(type: "TEXT", nullable: true),
                    ApiKey = table.Column<string>(type: "TEXT", nullable: true),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LlmProviderConfigs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LlmUsageLogs",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ProviderConfigId = table.Column<int>(type: "INTEGER", nullable: false),
                    ModelId = table.Column<string>(type: "TEXT", nullable: false),
                    Feature = table.Column<string>(type: "TEXT", nullable: true),
                    InputTokens = table.Column<int>(type: "INTEGER", nullable: false),
                    OutputTokens = table.Column<int>(type: "INTEGER", nullable: false),
                    TotalTokens = table.Column<int>(type: "INTEGER", nullable: false),
                    DurationMs = table.Column<int>(type: "INTEGER", nullable: false),
                    CacheHit = table.Column<bool>(type: "INTEGER", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    ErrorMessage = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    RequestSummary = table.Column<string>(type: "TEXT", nullable: true),
                    ResponseSummary = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LlmUsageLogs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AiFeatureConfigs_Feature",
                table: "AiFeatureConfigs",
                column: "Feature",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LlmCallCacheEntries_CacheKey",
                table: "LlmCallCacheEntries",
                column: "CacheKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LlmCallCacheEntries_ExpiresAt",
                table: "LlmCallCacheEntries",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_LlmProviderConfigs_IsEnabled",
                table: "LlmProviderConfigs",
                column: "IsEnabled");

            migrationBuilder.CreateIndex(
                name: "IX_LlmProviderConfigs_ProviderType",
                table: "LlmProviderConfigs",
                column: "ProviderType");

            migrationBuilder.CreateIndex(
                name: "IX_LlmUsageLogs_CacheHit",
                table: "LlmUsageLogs",
                column: "CacheHit");

            migrationBuilder.CreateIndex(
                name: "IX_LlmUsageLogs_CreatedAt",
                table: "LlmUsageLogs",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_LlmUsageLogs_Feature",
                table: "LlmUsageLogs",
                column: "Feature");

            migrationBuilder.CreateIndex(
                name: "IX_LlmUsageLogs_ProviderConfigId",
                table: "LlmUsageLogs",
                column: "ProviderConfigId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AiFeatureConfigs");

            migrationBuilder.DropTable(
                name: "LlmCallCacheEntries");

            migrationBuilder.DropTable(
                name: "LlmProviderConfigs");

            migrationBuilder.DropTable(
                name: "LlmUsageLogs");
        }
    }
}
