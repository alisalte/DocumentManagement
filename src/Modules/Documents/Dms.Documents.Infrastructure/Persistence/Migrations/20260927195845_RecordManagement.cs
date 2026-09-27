using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dms.Documents.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RecordManagement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "record_classes",
                schema: "documents",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_record_classes", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "record_series",
                schema: "documents",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    record_class_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_record_series", x => x.id);
                    table.ForeignKey(
                        name: "fk_record_series_class",
                        column: x => x.record_class_id,
                        principalSchema: "documents",
                        principalTable: "record_classes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "records",
                schema: "documents",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    final_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    record_class_id = table.Column<Guid>(type: "uuid", nullable: false),
                    record_series_id = table.Column<Guid>(type: "uuid", nullable: true),
                    title = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    declared_by = table.Column<Guid>(type: "uuid", nullable: false),
                    declared_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    metadata_frozen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_transition_reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_records", x => x.id);
                    table.CheckConstraint("ck_records_status", "status IN ('Active','UnderRetention','Expired','PendingDisposal','Destroyed')");
                    table.ForeignKey(
                        name: "fk_records_class",
                        column: x => x.record_class_id,
                        principalSchema: "documents",
                        principalTable: "record_classes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_records_document",
                        column: x => x.document_id,
                        principalSchema: "documents",
                        principalTable: "documents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_records_final_version",
                        column: x => x.final_version_id,
                        principalSchema: "documents",
                        principalTable: "document_versions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_records_series",
                        column: x => x.record_series_id,
                        principalSchema: "documents",
                        principalTable: "record_series",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ux_record_classes_code",
                schema: "documents",
                table: "record_classes",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_record_series_class_code",
                schema: "documents",
                table: "record_series",
                columns: new[] { "record_class_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_records_class",
                schema: "documents",
                table: "records",
                column: "record_class_id");

            migrationBuilder.CreateIndex(
                name: "IX_records_final_version_id",
                schema: "documents",
                table: "records",
                column: "final_version_id");

            migrationBuilder.CreateIndex(
                name: "IX_records_record_series_id",
                schema: "documents",
                table: "records",
                column: "record_series_id");

            migrationBuilder.CreateIndex(
                name: "ix_records_status",
                schema: "documents",
                table: "records",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ux_records_document",
                schema: "documents",
                table: "records",
                column: "document_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "records",
                schema: "documents");

            migrationBuilder.DropTable(
                name: "record_series",
                schema: "documents");

            migrationBuilder.DropTable(
                name: "record_classes",
                schema: "documents");
        }
    }
}
