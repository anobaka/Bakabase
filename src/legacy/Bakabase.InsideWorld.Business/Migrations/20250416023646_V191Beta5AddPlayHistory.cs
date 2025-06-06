﻿using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bakabase.InsideWorld.Business.Migrations
{
    /// <inheritdoc />
    public partial class V191Beta5AddPlayHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "PlayedAt",
                table: "ResourcesV2",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PlayHistories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ResourceId = table.Column<int>(type: "INTEGER", nullable: false),
                    Item = table.Column<string>(type: "TEXT", nullable: true),
                    PlayedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlayHistories", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PlayHistories_PlayedAt",
                table: "PlayHistories",
                column: "PlayedAt");

            migrationBuilder.CreateIndex(
                name: "IX_PlayHistories_ResourceId",
                table: "PlayHistories",
                column: "ResourceId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PlayHistories");

            migrationBuilder.DropColumn(
                name: "PlayedAt",
                table: "ResourcesV2");
        }
    }
}
