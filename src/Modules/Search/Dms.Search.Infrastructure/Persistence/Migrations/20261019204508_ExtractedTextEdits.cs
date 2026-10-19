using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dms.Search.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ExtractedTextEdits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "edited_at",
                schema: "search",
                table: "content_extractions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "edited_by",
                schema: "search",
                table: "content_extractions",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "edited_at",
                schema: "search",
                table: "content_extractions");

            migrationBuilder.DropColumn(
                name: "edited_by",
                schema: "search",
                table: "content_extractions");
        }
    }
}
