using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dms.Documents.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ImportValidationProgress : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "validated_items",
                schema: "documents",
                table: "import_jobs",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "validated_items",
                schema: "documents",
                table: "import_jobs");
        }
    }
}
