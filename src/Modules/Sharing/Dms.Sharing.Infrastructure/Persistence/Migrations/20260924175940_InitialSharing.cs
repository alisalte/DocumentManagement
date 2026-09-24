using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dms.Sharing.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialSharing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "sharing");

            migrationBuilder.CreateTable(
                name: "document_shares",
                schema: "sharing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    shared_by = table.Column<Guid>(type: "uuid", nullable: false),
                    shared_with_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    permissions = table.Column<short>(type: "smallint", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    message = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revoked_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_document_shares", x => x.id);
                    table.CheckConstraint("ck_document_shares_not_self", "shared_by <> shared_with_user_id");
                    table.CheckConstraint("ck_document_shares_permissions", "permissions BETWEEN 1 AND 7");
                    table.CheckConstraint("ck_document_shares_view", "(permissions & 1) = 1");
                });

            migrationBuilder.CreateTable(
                name: "share_links",
                schema: "sharing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_hash = table.Column<byte[]>(type: "bytea", nullable: false),
                    token_prefix = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    permissions = table.Column<short>(type: "smallint", nullable: false),
                    password_hash = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    max_access_count = table.Column<int>(type: "integer", nullable: true),
                    access_count = table.Column<int>(type: "integer", nullable: false),
                    failed_attempts = table.Column<int>(type: "integer", nullable: false),
                    locked_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    label = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revoked_by = table.Column<Guid>(type: "uuid", nullable: true),
                    last_accessed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_share_links", x => x.id);
                    table.CheckConstraint("ck_share_links_count", "access_count <= coalesce(max_access_count, access_count)");
                    table.CheckConstraint("ck_share_links_expiry", "expires_at > created_at");
                    table.CheckConstraint("ck_share_links_max_count", "max_access_count IS NULL OR max_access_count > 0");
                    table.CheckConstraint("ck_share_links_permissions", "permissions BETWEEN 1 AND 7");
                    table.CheckConstraint("ck_share_links_view", "(permissions & 1) = 1");
                });

            migrationBuilder.CreateTable(
                name: "share_link_sessions",
                schema: "sharing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    link_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_hash = table.Column<byte[]>(type: "bytea", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_share_link_sessions", x => x.id);
                    table.ForeignKey(
                        name: "fk_share_link_sessions_link",
                        column: x => x.link_id,
                        principalSchema: "sharing",
                        principalTable: "share_links",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_document_shares_document",
                schema: "sharing",
                table: "document_shares",
                column: "document_id");

            migrationBuilder.CreateIndex(
                name: "ix_document_shares_recipient",
                schema: "sharing",
                table: "document_shares",
                columns: new[] { "shared_with_user_id", "document_id" },
                filter: "revoked_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ux_document_shares_version_recipient",
                schema: "sharing",
                table: "document_shares",
                columns: new[] { "version_id", "shared_with_user_id" },
                unique: true,
                filter: "revoked_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_share_link_sessions_expires_at",
                schema: "sharing",
                table: "share_link_sessions",
                column: "expires_at");

            migrationBuilder.CreateIndex(
                name: "ix_share_link_sessions_link",
                schema: "sharing",
                table: "share_link_sessions",
                column: "link_id");

            migrationBuilder.CreateIndex(
                name: "ux_share_link_sessions_token_hash",
                schema: "sharing",
                table: "share_link_sessions",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_share_links_document",
                schema: "sharing",
                table: "share_links",
                column: "document_id");

            migrationBuilder.CreateIndex(
                name: "ux_share_links_token_hash",
                schema: "sharing",
                table: "share_links",
                column: "token_hash",
                unique: true);

            // Shares and links point at one version of one document (decision D8). Purging the
            // document takes them with it; nothing else ever deletes a version.
            migrationBuilder.Sql("""
                ALTER TABLE sharing.document_shares
                    ADD CONSTRAINT fk_document_shares_version FOREIGN KEY (version_id)
                        REFERENCES documents.document_versions (id) ON DELETE CASCADE,
                    ADD CONSTRAINT fk_document_shares_document FOREIGN KEY (document_id)
                        REFERENCES documents.documents (id) ON DELETE CASCADE;

                ALTER TABLE sharing.share_links
                    ADD CONSTRAINT fk_share_links_version FOREIGN KEY (version_id)
                        REFERENCES documents.document_versions (id) ON DELETE CASCADE,
                    ADD CONSTRAINT fk_share_links_document FOREIGN KEY (document_id)
                        REFERENCES documents.documents (id) ON DELETE CASCADE;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "document_shares",
                schema: "sharing");

            migrationBuilder.DropTable(
                name: "share_link_sessions",
                schema: "sharing");

            migrationBuilder.DropTable(
                name: "share_links",
                schema: "sharing");
        }
    }
}
