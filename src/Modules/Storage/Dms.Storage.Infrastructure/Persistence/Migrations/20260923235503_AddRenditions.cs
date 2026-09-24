using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dms.Storage.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRenditions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "renditions",
                schema: "storage",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_object_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    error = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_renditions", x => x.id);
                    table.ForeignKey(
                        name: "fk_renditions_source",
                        column: x => x.source_object_id,
                        principalSchema: "storage",
                        principalTable: "storage_objects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "rendition_pages",
                schema: "storage",
                columns: table => new
                {
                    rendition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    page_number = table.Column<int>(type: "integer", nullable: false),
                    object_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_rendition_pages", x => new { x.rendition_id, x.page_number });
                    table.ForeignKey(
                        name: "fk_rendition_pages_object",
                        column: x => x.object_id,
                        principalSchema: "storage",
                        principalTable: "storage_objects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_rendition_pages_rendition",
                        column: x => x.rendition_id,
                        principalSchema: "storage",
                        principalTable: "renditions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_rendition_pages_object_id",
                schema: "storage",
                table: "rendition_pages",
                column: "object_id");

            migrationBuilder.CreateIndex(
                name: "ux_renditions_source_kind",
                schema: "storage",
                table: "renditions",
                columns: new[] { "source_object_id", "kind" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "rendition_pages",
                schema: "storage");

            migrationBuilder.DropTable(
                name: "renditions",
                schema: "storage");
        }
    }
}
