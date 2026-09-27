using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dms.Documents.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RetentionAndLegalHold : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "retention_exception_reason",
                schema: "documents",
                table: "records",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "retention_expires_at",
                schema: "documents",
                table: "records",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "retention_period_days",
                schema: "documents",
                table: "records",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "retention_policy_id",
                schema: "documents",
                table: "records",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "retention_policy_version",
                schema: "documents",
                table: "records",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "retention_start_event",
                schema: "documents",
                table: "records",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "retention_started_at",
                schema: "documents",
                table: "records",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "legal_holds",
                schema: "documents",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    released_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    released_by = table.Column<Guid>(type: "uuid", nullable: true),
                    release_reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_legal_holds", x => x.id);
                    table.ForeignKey(
                        name: "fk_legal_holds_document",
                        column: x => x.document_id,
                        principalSchema: "documents",
                        principalTable: "documents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "retention_policies",
                schema: "documents",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    retention_period_days = table.Column<int>(type: "integer", nullable: false),
                    start_event = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    version_number = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_retention_policies", x => x.id);
                    table.CheckConstraint("ck_retention_policies_days", "retention_period_days > 0");
                    table.CheckConstraint("ck_retention_policies_version", "version_number > 0");
                });

            migrationBuilder.CreateIndex(
                name: "ix_records_retention_expires",
                schema: "documents",
                table: "records",
                column: "retention_expires_at",
                filter: "retention_expires_at IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_records_retention_policy_id",
                schema: "documents",
                table: "records",
                column: "retention_policy_id");

            migrationBuilder.CreateIndex(
                name: "ix_legal_holds_active",
                schema: "documents",
                table: "legal_holds",
                columns: new[] { "document_id", "released_at" },
                filter: "released_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_legal_holds_document",
                schema: "documents",
                table: "legal_holds",
                column: "document_id");

            migrationBuilder.CreateIndex(
                name: "ux_retention_policies_code",
                schema: "documents",
                table: "retention_policies",
                column: "code",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_records_retention_policy",
                schema: "documents",
                table: "records",
                column: "retention_policy_id",
                principalSchema: "documents",
                principalTable: "retention_policies",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_records_retention_policy",
                schema: "documents",
                table: "records");

            migrationBuilder.DropTable(
                name: "legal_holds",
                schema: "documents");

            migrationBuilder.DropTable(
                name: "retention_policies",
                schema: "documents");

            migrationBuilder.DropIndex(
                name: "ix_records_retention_expires",
                schema: "documents",
                table: "records");

            migrationBuilder.DropIndex(
                name: "IX_records_retention_policy_id",
                schema: "documents",
                table: "records");

            migrationBuilder.DropColumn(
                name: "retention_exception_reason",
                schema: "documents",
                table: "records");

            migrationBuilder.DropColumn(
                name: "retention_expires_at",
                schema: "documents",
                table: "records");

            migrationBuilder.DropColumn(
                name: "retention_period_days",
                schema: "documents",
                table: "records");

            migrationBuilder.DropColumn(
                name: "retention_policy_id",
                schema: "documents",
                table: "records");

            migrationBuilder.DropColumn(
                name: "retention_policy_version",
                schema: "documents",
                table: "records");

            migrationBuilder.DropColumn(
                name: "retention_start_event",
                schema: "documents",
                table: "records");

            migrationBuilder.DropColumn(
                name: "retention_started_at",
                schema: "documents",
                table: "records");
        }
    }
}
