using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dms.Documents.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FiscalYear : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "fiscal_year",
                schema: "documents",
                table: "documents",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "ix_documents_fiscal_year_category",
                schema: "documents",
                table: "documents",
                columns: new[] { "fiscal_year", "category_id" },
                filter: "deleted_at IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_documents_fiscal_year_category",
                schema: "documents",
                table: "documents");

            migrationBuilder.DropColumn(
                name: "fiscal_year",
                schema: "documents",
                table: "documents");
        }
    }
}
