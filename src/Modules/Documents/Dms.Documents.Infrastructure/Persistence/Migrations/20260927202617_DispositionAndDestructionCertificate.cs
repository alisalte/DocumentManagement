using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dms.Documents.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DispositionAndDestructionCertificate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "dispositions",
                schema: "documents",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    record_id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    request_reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    requested_by = table.Column<Guid>(type: "uuid", nullable: false),
                    requested_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    reviewed_by = table.Column<Guid>(type: "uuid", nullable: true),
                    reviewed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    decision_reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    approved_by = table.Column<Guid>(type: "uuid", nullable: true),
                    approved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    destroyed_by = table.Column<Guid>(type: "uuid", nullable: true),
                    destroyed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_dispositions", x => x.id);
                    table.CheckConstraint("ck_dispositions_status", "status IN ('PendingReview','Approved','Rejected','Destroyed')");
                    table.ForeignKey(
                        name: "fk_dispositions_document",
                        column: x => x.document_id,
                        principalSchema: "documents",
                        principalTable: "documents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_dispositions_record",
                        column: x => x.record_id,
                        principalSchema: "documents",
                        principalTable: "records",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "destruction_certificates",
                schema: "documents",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    certificate_number = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    disposition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    record_id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    final_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    final_version_label = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    record_title = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    content_sha256 = table.Column<byte[]>(type: "bytea", nullable: false),
                    retention_policy_id = table.Column<Guid>(type: "uuid", nullable: true),
                    retention_policy_version = table.Column<int>(type: "integer", nullable: true),
                    retention_expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    legal_hold_checked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    approved_by = table.Column<Guid>(type: "uuid", nullable: false),
                    approved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    destroyed_by = table.Column<Guid>(type: "uuid", nullable: false),
                    destroyed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    certificate_hash = table.Column<byte[]>(type: "bytea", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_destruction_certificates", x => x.id);
                    table.CheckConstraint("ck_destruction_certificates_sha256", "octet_length(content_sha256) = 32 AND octet_length(certificate_hash) = 32");
                    table.ForeignKey(
                        name: "fk_destruction_certificates_disposition",
                        column: x => x.disposition_id,
                        principalSchema: "documents",
                        principalTable: "dispositions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_destruction_certificates_record",
                        column: x => x.record_id,
                        principalSchema: "documents",
                        principalTable: "records",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ux_destruction_certificates_disposition",
                schema: "documents",
                table: "destruction_certificates",
                column: "disposition_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_destruction_certificates_number",
                schema: "documents",
                table: "destruction_certificates",
                column: "certificate_number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_destruction_certificates_record",
                schema: "documents",
                table: "destruction_certificates",
                column: "record_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_dispositions_document",
                schema: "documents",
                table: "dispositions",
                column: "document_id");

            migrationBuilder.CreateIndex(
                name: "ix_dispositions_status",
                schema: "documents",
                table: "dispositions",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ux_dispositions_active_record",
                schema: "documents",
                table: "dispositions",
                column: "record_id",
                unique: true,
                filter: "status IN ('PendingReview','Approved')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "destruction_certificates",
                schema: "documents");

            migrationBuilder.DropTable(
                name: "dispositions",
                schema: "documents");
        }
    }
}
