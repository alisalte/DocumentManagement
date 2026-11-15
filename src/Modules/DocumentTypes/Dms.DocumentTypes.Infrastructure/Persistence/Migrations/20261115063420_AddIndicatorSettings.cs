using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dms.DocumentTypes.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddIndicatorSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "indicator_settings",
                schema: "doctypes",
                columns: table => new
                {
                    id = table.Column<short>(type: "smallint", nullable: false),
                    default_pattern = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_indicator_settings", x => x.id);
                    table.CheckConstraint("ck_indicator_settings_single_row", "id = 1");
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "indicator_settings",
                schema: "doctypes");
        }
    }
}
