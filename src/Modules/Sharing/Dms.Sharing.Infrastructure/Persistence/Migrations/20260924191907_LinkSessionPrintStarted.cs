using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dms.Sharing.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class LinkSessionPrintStarted : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "print_started_at",
                schema: "sharing",
                table: "share_link_sessions",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "print_started_at",
                schema: "sharing",
                table: "share_link_sessions");
        }
    }
}
