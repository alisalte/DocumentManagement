using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dms.Authorization.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialAuthorization : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "authz");

            migrationBuilder.CreateTable(
                name: "permissions",
                schema: "authz",
                columns: table => new
                {
                    code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    scope = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    requires_view = table.Column<bool>(type: "boolean", nullable: false),
                    description = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_permissions", x => x.code);
                });

            migrationBuilder.CreateTable(
                name: "roles",
                schema: "authz",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    is_system = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_roles", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "resource_permissions",
                schema: "authz",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    resource_type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    resource_id = table.Column<Guid>(type: "uuid", nullable: false),
                    subject_type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    subject_id = table.Column<Guid>(type: "uuid", nullable: false),
                    permission_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    effect = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    inherit = table.Column<bool>(type: "boolean", nullable: false),
                    reason = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_resource_permissions", x => x.id);
                    table.CheckConstraint("ck_resource_permissions_inherit", "inherit = false OR resource_type = 'Category'");
                    table.ForeignKey(
                        name: "FK_resource_permissions_permissions_permission_code",
                        column: x => x.permission_code,
                        principalSchema: "authz",
                        principalTable: "permissions",
                        principalColumn: "code",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "role_permissions",
                schema: "authz",
                columns: table => new
                {
                    role_id = table.Column<Guid>(type: "uuid", nullable: false),
                    permission_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_role_permissions", x => new { x.role_id, x.permission_code });
                    table.ForeignKey(
                        name: "FK_role_permissions_permissions_permission_code",
                        column: x => x.permission_code,
                        principalSchema: "authz",
                        principalTable: "permissions",
                        principalColumn: "code",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_role_permissions_roles_role_id",
                        column: x => x.role_id,
                        principalSchema: "authz",
                        principalTable: "roles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_roles",
                schema: "authz",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role_id = table.Column<Guid>(type: "uuid", nullable: false),
                    granted_by = table.Column<Guid>(type: "uuid", nullable: false),
                    granted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_roles", x => new { x.user_id, x.role_id });
                    table.ForeignKey(
                        name: "FK_user_roles_roles_role_id",
                        column: x => x.role_id,
                        principalSchema: "authz",
                        principalTable: "roles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_resource_permissions_permission_code",
                schema: "authz",
                table: "resource_permissions",
                column: "permission_code");

            migrationBuilder.CreateIndex(
                name: "ix_resource_permissions_resource",
                schema: "authz",
                table: "resource_permissions",
                columns: new[] { "resource_type", "resource_id" });

            migrationBuilder.CreateIndex(
                name: "ix_resource_permissions_subject",
                schema: "authz",
                table: "resource_permissions",
                columns: new[] { "subject_type", "subject_id" });

            migrationBuilder.CreateIndex(
                name: "ux_resource_permissions_tuple",
                schema: "authz",
                table: "resource_permissions",
                columns: new[] { "resource_type", "resource_id", "subject_type", "subject_id", "permission_code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_role_permissions_permission_code",
                schema: "authz",
                table: "role_permissions",
                column: "permission_code");

            migrationBuilder.CreateIndex(
                name: "ux_roles_code",
                schema: "authz",
                table: "roles",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_user_roles_role",
                schema: "authz",
                table: "user_roles",
                column: "role_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "resource_permissions",
                schema: "authz");

            migrationBuilder.DropTable(
                name: "role_permissions",
                schema: "authz");

            migrationBuilder.DropTable(
                name: "user_roles",
                schema: "authz");

            migrationBuilder.DropTable(
                name: "permissions",
                schema: "authz");

            migrationBuilder.DropTable(
                name: "roles",
                schema: "authz");
        }
    }
}
