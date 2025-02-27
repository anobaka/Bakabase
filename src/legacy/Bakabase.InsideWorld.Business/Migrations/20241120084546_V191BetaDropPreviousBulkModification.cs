﻿using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bakabase.InsideWorld.Business.Migrations
{
    /// <inheritdoc />
    public partial class V191BetaDropPreviousBulkModification : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BulkModificationDiffs");

            migrationBuilder.DropTable(
                name: "BulkModifications");

            migrationBuilder.DropTable(
                name: "BulkModificationTempData");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BulkModificationDiffs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    BulkModificationId = table.Column<int>(type: "INTEGER", nullable: false),
                    CurrentValue = table.Column<string>(type: "TEXT", nullable: true),
                    NewValue = table.Column<string>(type: "TEXT", nullable: true),
                    Operation = table.Column<int>(type: "INTEGER", nullable: false),
                    Property = table.Column<int>(type: "INTEGER", nullable: false),
                    PropertyKey = table.Column<string>(type: "TEXT", nullable: true),
                    ResourceId = table.Column<int>(type: "INTEGER", nullable: false),
                    ResourcePath = table.Column<string>(type: "TEXT", nullable: false),
                    Type = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BulkModificationDiffs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BulkModifications",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    AppliedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CalculatedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Filter = table.Column<string>(type: "TEXT", nullable: true),
                    FilteredAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Processes = table.Column<string>(type: "TEXT", nullable: true),
                    RevertedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    Variables = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BulkModifications", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BulkModificationTempData",
                columns: table => new
                {
                    BulkModificationId = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ResourceIds = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BulkModificationTempData", x => x.BulkModificationId);
                });
        }
    }
}
