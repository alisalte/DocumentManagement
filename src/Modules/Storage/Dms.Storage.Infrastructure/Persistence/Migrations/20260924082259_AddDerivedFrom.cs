using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dms.Storage.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDerivedFrom : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "derived_from_id",
                schema: "storage",
                table: "storage_objects",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_storage_objects_derived_from",
                schema: "storage",
                table: "storage_objects",
                column: "derived_from_id",
                filter: "derived_from_id IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_storage_objects_derived_from",
                schema: "storage",
                table: "storage_objects");

            migrationBuilder.DropColumn(
                name: "derived_from_id",
                schema: "storage",
                table: "storage_objects");
        }
    }
}
