using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bakabase.InsideWorld.Business.Migrations
{
    /// <inheritdoc />
    public partial class AddResourceMovePanel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "CanOverwrite",
                table: "ResourceMoveRecords",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "CancelRequested",
                table: "ResourceMoveRecords",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "ConflictDecisionsJson",
                table: "ResourceMoveRecords",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ConflictFingerprint",
                table: "ResourceMoveRecords",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ConflictKind",
                table: "ResourceMoveRecords",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ConflictPath",
                table: "ResourceMoveRecords",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ConflictPolicy",
                table: "ResourceMoveRecords",
                type: "TEXT",
                nullable: false,
                defaultValue: "inherit");

            migrationBuilder.AddColumn<int>(
                name: "ConflictVersion",
                table: "ResourceMoveRecords",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "DestinationId",
                table: "ResourceMoveRecords",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DestinationName",
                table: "ResourceMoveRecords",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ErrorCode",
                table: "ResourceMoveRecords",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IdempotencyKey",
                table: "ResourceMoveRecords",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MoveJournalJson",
                table: "ResourceMoveRecords",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Origin",
                table: "ResourceMoveRecords",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PolicyAuditJson",
                table: "ResourceMoveRecords",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RequestFingerprint",
                table: "ResourceMoveRecords",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReservedResourceIdsJson",
                table: "ResourceMoveRecords",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceResourcePathsJson",
                table: "ResourceMoveRecords",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceTabId",
                table: "ResourceMoveRecords",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceTabName",
                table: "ResourceMoveRecords",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ResourceMoveRecords_IdempotencyKey",
                table: "ResourceMoveRecords",
                column: "IdempotencyKey",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ResourceMoveRecords_IdempotencyKey",
                table: "ResourceMoveRecords");

            migrationBuilder.DropColumn(
                name: "CanOverwrite",
                table: "ResourceMoveRecords");

            migrationBuilder.DropColumn(
                name: "CancelRequested",
                table: "ResourceMoveRecords");

            migrationBuilder.DropColumn(
                name: "ConflictDecisionsJson",
                table: "ResourceMoveRecords");

            migrationBuilder.DropColumn(
                name: "ConflictFingerprint",
                table: "ResourceMoveRecords");

            migrationBuilder.DropColumn(
                name: "ConflictKind",
                table: "ResourceMoveRecords");

            migrationBuilder.DropColumn(
                name: "ConflictPath",
                table: "ResourceMoveRecords");

            migrationBuilder.DropColumn(
                name: "ConflictPolicy",
                table: "ResourceMoveRecords");

            migrationBuilder.DropColumn(
                name: "ConflictVersion",
                table: "ResourceMoveRecords");

            migrationBuilder.DropColumn(
                name: "DestinationId",
                table: "ResourceMoveRecords");

            migrationBuilder.DropColumn(
                name: "DestinationName",
                table: "ResourceMoveRecords");

            migrationBuilder.DropColumn(
                name: "ErrorCode",
                table: "ResourceMoveRecords");

            migrationBuilder.DropColumn(
                name: "IdempotencyKey",
                table: "ResourceMoveRecords");

            migrationBuilder.DropColumn(
                name: "MoveJournalJson",
                table: "ResourceMoveRecords");

            migrationBuilder.DropColumn(
                name: "Origin",
                table: "ResourceMoveRecords");

            migrationBuilder.DropColumn(
                name: "PolicyAuditJson",
                table: "ResourceMoveRecords");

            migrationBuilder.DropColumn(
                name: "RequestFingerprint",
                table: "ResourceMoveRecords");

            migrationBuilder.DropColumn(
                name: "ReservedResourceIdsJson",
                table: "ResourceMoveRecords");

            migrationBuilder.DropColumn(
                name: "SourceResourcePathsJson",
                table: "ResourceMoveRecords");

            migrationBuilder.DropColumn(
                name: "SourceTabId",
                table: "ResourceMoveRecords");

            migrationBuilder.DropColumn(
                name: "SourceTabName",
                table: "ResourceMoveRecords");
        }
    }
}
