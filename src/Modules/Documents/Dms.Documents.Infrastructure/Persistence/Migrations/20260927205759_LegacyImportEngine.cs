using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dms.Documents.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class LegacyImportEngine : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "classification_levels",
                schema: "documents",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    rank = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_classification_levels", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "import_jobs",
                schema: "documents",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_system = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    files_root = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    manifest_sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    failure_policy = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    create_missing_categories = table.Column<bool>(type: "boolean", nullable: false),
                    dry_run_only = table.Column<bool>(type: "boolean", nullable: false),
                    total_items = table.Column<int>(type: "integer", nullable: false),
                    processed_items = table.Column<int>(type: "integer", nullable: false),
                    succeeded_items = table.Column<int>(type: "integer", nullable: false),
                    failed_items = table.Column<int>(type: "integer", nullable: false),
                    skipped_items = table.Column<int>(type: "integer", nullable: false),
                    invalid_items = table.Column<int>(type: "integer", nullable: false),
                    bytes_processed = table.Column<long>(type: "bigint", nullable: false),
                    bytes_total = table.Column<long>(type: "bigint", nullable: false),
                    last_error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_import_jobs", x => x.id);
                    table.CheckConstraint("ck_import_jobs_failure_policy", "failure_policy IN ('ContinueOnError','StopOnError')");
                    table.CheckConstraint("ck_import_jobs_status", "status IN ('Created','Validating','ValidationFailed','Ready','Running','Paused','Completed','CompletedWithErrors','Failed')");
                });

            migrationBuilder.CreateTable(
                name: "import_source_index",
                schema: "documents",
                columns: table => new
                {
                    source_system = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    source_id = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    import_job_id = table.Column<Guid>(type: "uuid", nullable: false),
                    import_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    imported_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_import_source_index", x => new { x.source_system, x.source_id });
                });

            migrationBuilder.CreateTable(
                name: "document_classifications",
                schema: "documents",
                columns: table => new
                {
                    document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    level_id = table.Column<Guid>(type: "uuid", nullable: false),
                    assigned_by = table.Column<Guid>(type: "uuid", nullable: false),
                    assigned_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_document_classifications", x => x.document_id);
                    table.ForeignKey(
                        name: "fk_document_classifications_document",
                        column: x => x.document_id,
                        principalSchema: "documents",
                        principalTable: "documents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_document_classifications_level",
                        column: x => x.level_id,
                        principalSchema: "documents",
                        principalTable: "classification_levels",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "import_items",
                schema: "documents",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    import_job_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_id = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    source_path = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    title = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    entry_json = table.Column<string>(type: "jsonb", nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    error_code = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    error_message = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    expected_sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    actual_sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    expected_size = table.Column<long>(type: "bigint", nullable: true),
                    actual_size = table.Column<long>(type: "bigint", nullable: true),
                    target_document_id = table.Column<Guid>(type: "uuid", nullable: true),
                    target_record_id = table.Column<Guid>(type: "uuid", nullable: true),
                    target_version_id = table.Column<Guid>(type: "uuid", nullable: true),
                    retry_count = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_import_items", x => x.id);
                    table.CheckConstraint("ck_import_items_status", "status IN ('Pending','Valid','Invalid','Ready','Running','Succeeded','Failed','Skipped','Retryable')");
                    table.ForeignKey(
                        name: "fk_import_items_job",
                        column: x => x.import_job_id,
                        principalSchema: "documents",
                        principalTable: "import_jobs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "import_mappings",
                schema: "documents",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    import_job_id = table.Column<Guid>(type: "uuid", nullable: true),
                    source_system = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    source_key = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    target_key = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_import_mappings", x => x.id);
                    table.ForeignKey(
                        name: "fk_import_mappings_job",
                        column: x => x.import_job_id,
                        principalSchema: "documents",
                        principalTable: "import_jobs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ux_classification_levels_code",
                schema: "documents",
                table: "classification_levels",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_document_classifications_level",
                schema: "documents",
                table: "document_classifications",
                column: "level_id");

            migrationBuilder.CreateIndex(
                name: "ix_import_items_claimable",
                schema: "documents",
                table: "import_items",
                column: "status",
                filter: "status IN ('Ready','Retryable','Running')");

            migrationBuilder.CreateIndex(
                name: "ix_import_items_job_status",
                schema: "documents",
                table: "import_items",
                columns: new[] { "import_job_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ux_import_items_job_source",
                schema: "documents",
                table: "import_items",
                columns: new[] { "import_job_id", "source_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_import_jobs_created_at",
                schema: "documents",
                table: "import_jobs",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_import_jobs_source_created",
                schema: "documents",
                table: "import_jobs",
                columns: new[] { "source_system", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_import_jobs_status",
                schema: "documents",
                table: "import_jobs",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "IX_import_mappings_import_job_id",
                schema: "documents",
                table: "import_mappings",
                column: "import_job_id");

            migrationBuilder.CreateIndex(
                name: "ux_import_mappings_lookup",
                schema: "documents",
                table: "import_mappings",
                columns: new[] { "source_system", "import_job_id", "kind", "source_key" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "ix_import_source_index_document",
                schema: "documents",
                table: "import_source_index",
                column: "document_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "document_classifications",
                schema: "documents");

            migrationBuilder.DropTable(
                name: "import_items",
                schema: "documents");

            migrationBuilder.DropTable(
                name: "import_mappings",
                schema: "documents");

            migrationBuilder.DropTable(
                name: "import_source_index",
                schema: "documents");

            migrationBuilder.DropTable(
                name: "classification_levels",
                schema: "documents");

            migrationBuilder.DropTable(
                name: "import_jobs",
                schema: "documents");
        }
    }
}
