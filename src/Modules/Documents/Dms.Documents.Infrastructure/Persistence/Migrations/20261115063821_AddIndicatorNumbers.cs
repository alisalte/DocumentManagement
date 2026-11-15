using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dms.Documents.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddIndicatorNumbers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "indicator_number",
                schema: "documents",
                table: "documents",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "indicator_registered_at",
                schema: "documents",
                table: "documents",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "indicator_registered_by",
                schema: "documents",
                table: "documents",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "indicator_sequence",
                schema: "documents",
                table: "documents",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "indicator_counters",
                schema: "documents",
                columns: table => new
                {
                    document_type_id = table.Column<Guid>(type: "uuid", nullable: false),
                    fiscal_year = table.Column<int>(type: "integer", nullable: false),
                    last_value = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_indicator_counters", x => new { x.document_type_id, x.fiscal_year });
                    table.CheckConstraint("ck_indicator_counters_positive", "last_value > 0");
                });

            // Case-insensitive and including deleted documents: a number is never issued twice (BR-5, BR-14).
            migrationBuilder.Sql("""
                CREATE UNIQUE INDEX ux_documents_indicator_number
                    ON documents.documents (upper(indicator_number))
                 WHERE indicator_number IS NOT NULL;

                ALTER TABLE documents.documents
                  ADD CONSTRAINT ck_documents_indicator_consistent
                CHECK ((indicator_number IS NULL) = (indicator_sequence IS NULL)
                   AND (indicator_number IS NULL) = (indicator_registered_at IS NULL));
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP INDEX IF EXISTS documents.ux_documents_indicator_number;
                ALTER TABLE documents.documents DROP CONSTRAINT IF EXISTS ck_documents_indicator_consistent;
                """);

            migrationBuilder.DropTable(
                name: "indicator_counters",
                schema: "documents");

            migrationBuilder.DropColumn(
                name: "indicator_number",
                schema: "documents",
                table: "documents");

            migrationBuilder.DropColumn(
                name: "indicator_registered_at",
                schema: "documents",
                table: "documents");

            migrationBuilder.DropColumn(
                name: "indicator_registered_by",
                schema: "documents",
                table: "documents");

            migrationBuilder.DropColumn(
                name: "indicator_sequence",
                schema: "documents",
                table: "documents");
        }
    }
}
