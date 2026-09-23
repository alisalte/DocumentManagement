using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dms.Storage.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialStorage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "storage");

            migrationBuilder.CreateTable(
                name: "storage_objects",
                schema: "storage",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    bucket = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    object_key = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    purpose = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    original_file_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    detected_mime_type = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    declared_mime_type = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    size = table.Column<long>(type: "bigint", nullable: false),
                    sha256 = table.Column<byte[]>(type: "bytea", nullable: false),
                    status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    scan_status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    scanned_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    committed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_storage_objects", x => x.id);
                    table.CheckConstraint("ck_storage_objects_sha256", "octet_length(sha256) = 32");
                    table.CheckConstraint("ck_storage_objects_size", "size >= 0");
                });

            migrationBuilder.CreateIndex(
                name: "ix_storage_objects_pending_deletion",
                schema: "storage",
                table: "storage_objects",
                column: "status",
                filter: "status = 'PendingDeletion'");

            migrationBuilder.CreateIndex(
                name: "ix_storage_objects_sha256",
                schema: "storage",
                table: "storage_objects",
                column: "sha256");

            migrationBuilder.CreateIndex(
                name: "ix_storage_objects_staged",
                schema: "storage",
                table: "storage_objects",
                column: "created_at",
                filter: "status = 'Staged'");

            migrationBuilder.CreateIndex(
                name: "ux_storage_objects_key",
                schema: "storage",
                table: "storage_objects",
                columns: new[] { "provider", "bucket", "object_key" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "storage_objects",
                schema: "storage");
        }
    }
}
