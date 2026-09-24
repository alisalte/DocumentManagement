using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dms.Search.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialSearch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "search");

            migrationBuilder.CreateTable(
                name: "content_extractions",
                schema: "search",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    storage_object_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    method = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    text_object_id = table.Column<Guid>(type: "uuid", nullable: true),
                    char_count = table.Column<int>(type: "integer", nullable: false),
                    engine = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    last_error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_content_extractions", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_content_extractions_status",
                schema: "search",
                table: "content_extractions",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ux_content_extractions_object",
                schema: "search",
                table: "content_extractions",
                column: "storage_object_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "content_extractions",
                schema: "search");
        }
    }
}
