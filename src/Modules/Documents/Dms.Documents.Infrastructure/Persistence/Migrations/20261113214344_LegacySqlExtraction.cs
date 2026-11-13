using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dms.Documents.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class LegacySqlExtraction : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "legacy_extractions",
                schema: "documents",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    server = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    database_name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    sql_user = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    staging_folder = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    cancel_requested = table.Column<bool>(type: "boolean", nullable: false),
                    files_total = table.Column<int>(type: "integer", nullable: false),
                    files_done = table.Column<int>(type: "integer", nullable: false),
                    files_reused = table.Column<int>(type: "integer", nullable: false),
                    files_empty = table.Column<int>(type: "integer", nullable: false),
                    bytes_total = table.Column<long>(type: "bigint", nullable: false),
                    bytes_done = table.Column<long>(type: "bigint", nullable: false),
                    warnings = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    last_error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    import_job_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    heartbeat_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_legacy_extractions", x => x.id);
                    table.CheckConstraint("ck_legacy_extractions_status", "status IN ('Queued','Running','Completed','Failed','Cancelled')");
                    table.ForeignKey(
                        name: "FK_legacy_extractions_import_jobs_import_job_id",
                        column: x => x.import_job_id,
                        principalSchema: "documents",
                        principalTable: "import_jobs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "ix_legacy_extractions_created_at",
                schema: "documents",
                table: "legacy_extractions",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "IX_legacy_extractions_import_job_id",
                schema: "documents",
                table: "legacy_extractions",
                column: "import_job_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "legacy_extractions",
                schema: "documents");
        }
    }
}
