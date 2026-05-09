using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bakabase.InsideWorld.Business.Migrations
{
    /// <inheritdoc />
    public partial class AddAigcIntegration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LlmProviderConfigs");

            migrationBuilder.CreateTable(
                name: "AigcArtifacts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    RunId = table.Column<int>(type: "INTEGER", nullable: false),
                    GeneratorId = table.Column<int>(type: "INTEGER", nullable: false),
                    OrdinalInRun = table.Column<int>(type: "INTEGER", nullable: false),
                    RelativePath = table.Column<string>(type: "TEXT", nullable: false),
                    ResourceId = table.Column<int>(type: "INTEGER", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AigcArtifacts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AigcGenerationRuns",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    GeneratorId = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    Prompt = table.Column<string>(type: "TEXT", nullable: true),
                    NegativePrompt = table.Column<string>(type: "TEXT", nullable: true),
                    RequestPayload = table.Column<string>(type: "TEXT", nullable: true),
                    ResponsePayload = table.Column<string>(type: "TEXT", nullable: true),
                    ErrorMessage = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AigcGenerationRuns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AigcGeneratorPropertyPresets",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    GeneratorId = table.Column<int>(type: "INTEGER", nullable: false),
                    Pool = table.Column<int>(type: "INTEGER", nullable: false),
                    PropertyId = table.Column<int>(type: "INTEGER", nullable: false),
                    SerializedBizValue = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AigcGeneratorPropertyPresets", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AigcGenerators",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    ProviderId = table.Column<int>(type: "INTEGER", nullable: false),
                    MediaType = table.Column<int>(type: "INTEGER", nullable: false),
                    PromptTemplate = table.Column<string>(type: "TEXT", nullable: true),
                    NegativePromptTemplate = table.Column<string>(type: "TEXT", nullable: true),
                    ParametersJson = table.Column<string>(type: "TEXT", nullable: true),
                    FilenameTemplate = table.Column<string>(type: "TEXT", nullable: false),
                    ResourceMode = table.Column<int>(type: "INTEGER", nullable: false),
                    AllowDeletion = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AigcGenerators", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AiProviders",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Kind = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Endpoint = table.Column<string>(type: "TEXT", nullable: true),
                    ApiKey = table.Column<string>(type: "TEXT", nullable: true),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    LlmEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    AigcEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    AigcConfigJson = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiProviders", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AigcArtifacts_GeneratorId",
                table: "AigcArtifacts",
                column: "GeneratorId");

            migrationBuilder.CreateIndex(
                name: "IX_AigcArtifacts_ResourceId",
                table: "AigcArtifacts",
                column: "ResourceId");

            migrationBuilder.CreateIndex(
                name: "IX_AigcArtifacts_RunId",
                table: "AigcArtifacts",
                column: "RunId");

            migrationBuilder.CreateIndex(
                name: "IX_AigcGenerationRuns_CreatedAt",
                table: "AigcGenerationRuns",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_AigcGenerationRuns_GeneratorId",
                table: "AigcGenerationRuns",
                column: "GeneratorId");

            migrationBuilder.CreateIndex(
                name: "IX_AigcGenerationRuns_Status",
                table: "AigcGenerationRuns",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_AigcGeneratorPropertyPresets_GeneratorId",
                table: "AigcGeneratorPropertyPresets",
                column: "GeneratorId");

            migrationBuilder.CreateIndex(
                name: "IX_AigcGeneratorPropertyPresets_GeneratorId_Pool_PropertyId",
                table: "AigcGeneratorPropertyPresets",
                columns: new[] { "GeneratorId", "Pool", "PropertyId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AigcGenerators_IsEnabled",
                table: "AigcGenerators",
                column: "IsEnabled");

            migrationBuilder.CreateIndex(
                name: "IX_AigcGenerators_ProviderId",
                table: "AigcGenerators",
                column: "ProviderId");

            migrationBuilder.CreateIndex(
                name: "IX_AiProviders_AigcEnabled",
                table: "AiProviders",
                column: "AigcEnabled");

            migrationBuilder.CreateIndex(
                name: "IX_AiProviders_IsEnabled",
                table: "AiProviders",
                column: "IsEnabled");

            migrationBuilder.CreateIndex(
                name: "IX_AiProviders_Kind",
                table: "AiProviders",
                column: "Kind");

            migrationBuilder.CreateIndex(
                name: "IX_AiProviders_LlmEnabled",
                table: "AiProviders",
                column: "LlmEnabled");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AigcArtifacts");

            migrationBuilder.DropTable(
                name: "AigcGenerationRuns");

            migrationBuilder.DropTable(
                name: "AigcGeneratorPropertyPresets");

            migrationBuilder.DropTable(
                name: "AigcGenerators");

            migrationBuilder.DropTable(
                name: "AiProviders");

            migrationBuilder.CreateTable(
                name: "LlmProviderConfigs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ApiKey = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Endpoint = table.Column<string>(type: "TEXT", nullable: true),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    ProviderType = table.Column<int>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LlmProviderConfigs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LlmProviderConfigs_IsEnabled",
                table: "LlmProviderConfigs",
                column: "IsEnabled");

            migrationBuilder.CreateIndex(
                name: "IX_LlmProviderConfigs_ProviderType",
                table: "LlmProviderConfigs",
                column: "ProviderType");
        }
    }
}
