using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dms.Documents.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialDocuments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "documents");

            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:ltree", ",,")
                .Annotation("Npgsql:PostgresExtension:pg_trgm", ",,");

            migrationBuilder.CreateTable(
                name: "categories",
                schema: "documents",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    parent_id = table.Column<Guid>(type: "uuid", nullable: true),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    path = table.Column<string>(type: "ltree", nullable: false),
                    depth = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_categories", x => x.id);
                    table.CheckConstraint("ck_categories_depth", "depth BETWEEN 0 AND 12");
                    table.CheckConstraint("ck_categories_not_own_parent", "parent_id <> id");
                    table.ForeignKey(
                        name: "fk_categories_parent",
                        column: x => x.parent_id,
                        principalSchema: "documents",
                        principalTable: "categories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "tags",
                schema: "documents",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    normalized_name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tags", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "documents",
                schema: "documents",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    description = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    document_type_id = table.Column<Guid>(type: "uuid", nullable: false),
                    category_id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    current_version_id = table.Column<Guid>(type: "uuid", nullable: true),
                    effective_version_id = table.Column<Guid>(type: "uuid", nullable: true),
                    latest_version_number = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<Guid>(type: "uuid", nullable: true),
                    delete_reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_documents", x => x.id);
                    table.CheckConstraint("ck_documents_deleted_consistent", "(deleted_at IS NULL) = (deleted_by IS NULL)");
                    table.CheckConstraint("ck_documents_latest_version", "latest_version_number >= 0");
                    table.ForeignKey(
                        name: "fk_documents_category",
                        column: x => x.category_id,
                        principalSchema: "documents",
                        principalTable: "categories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "document_tags",
                schema: "documents",
                columns: table => new
                {
                    document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tag_id = table.Column<Guid>(type: "uuid", nullable: false),
                    added_by = table.Column<Guid>(type: "uuid", nullable: false),
                    added_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_document_tags", x => new { x.document_id, x.tag_id });
                    table.ForeignKey(
                        name: "fk_document_tags_document",
                        column: x => x.document_id,
                        principalSchema: "documents",
                        principalTable: "documents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_document_tags_tag",
                        column: x => x.tag_id,
                        principalSchema: "documents",
                        principalTable: "tags",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "document_versions",
                schema: "documents",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version_number = table.Column<int>(type: "integer", nullable: false),
                    revision_number = table.Column<int>(type: "integer", nullable: false),
                    storage_object_id = table.Column<Guid>(type: "uuid", nullable: false),
                    file_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    mime_type = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    file_size = table.Column<long>(type: "bigint", nullable: false),
                    sha256 = table.Column<byte[]>(type: "bytea", nullable: false),
                    document_type_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    dynamic_data = table.Column<string>(type: "jsonb", nullable: false),
                    change_kind = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    change_description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    approval_status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    approved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_document_versions", x => x.id);
                    table.CheckConstraint("ck_document_versions_number", "version_number > 0 AND revision_number > 0");
                    table.CheckConstraint("ck_document_versions_sha256", "octet_length(sha256) = 32");
                    table.CheckConstraint("ck_document_versions_size", "file_size >= 0");
                    table.ForeignKey(
                        name: "fk_document_versions_document",
                        column: x => x.document_id,
                        principalSchema: "documents",
                        principalTable: "documents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_categories_path",
                schema: "documents",
                table: "categories",
                column: "path")
                .Annotation("Npgsql:IndexMethod", "gist");

            migrationBuilder.CreateIndex(
                name: "ux_categories_parent_code",
                schema: "documents",
                table: "categories",
                columns: new[] { "parent_id", "code" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "ux_categories_parent_name",
                schema: "documents",
                table: "categories",
                columns: new[] { "parent_id", "name" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "ix_document_tags_tag",
                schema: "documents",
                table: "document_tags",
                column: "tag_id");

            migrationBuilder.CreateIndex(
                name: "ix_document_versions_dynamic_data",
                schema: "documents",
                table: "document_versions",
                column: "dynamic_data")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "jsonb_path_ops" });

            migrationBuilder.CreateIndex(
                name: "ix_document_versions_sha256",
                schema: "documents",
                table: "document_versions",
                column: "sha256");

            migrationBuilder.CreateIndex(
                name: "ix_document_versions_storage_object",
                schema: "documents",
                table: "document_versions",
                column: "storage_object_id");

            migrationBuilder.CreateIndex(
                name: "ux_document_versions_number",
                schema: "documents",
                table: "document_versions",
                columns: new[] { "document_id", "version_number", "revision_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_documents_category",
                schema: "documents",
                table: "documents",
                column: "category_id",
                filter: "deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_documents_owner",
                schema: "documents",
                table: "documents",
                column: "owner_id",
                filter: "deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_documents_title_trgm",
                schema: "documents",
                table: "documents",
                column: "title")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "ix_documents_trash",
                schema: "documents",
                table: "documents",
                column: "deleted_at",
                filter: "deleted_at IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_documents_type",
                schema: "documents",
                table: "documents",
                column: "document_type_id",
                filter: "deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_documents_updated_at",
                schema: "documents",
                table: "documents",
                column: "updated_at",
                filter: "deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ux_tags_normalized_name",
                schema: "documents",
                table: "tags",
                column: "normalized_name",
                unique: true);

            // Hand-written parts EF cannot express. Kept in this migration so the schema is never
            // observable without them.

            // A document and its V1 point at each other and are inserted in one transaction, so
            // the two version pointers are checked at commit, not per statement.
            migrationBuilder.Sql("""
                ALTER TABLE documents.documents
                    ADD CONSTRAINT fk_documents_current_version FOREIGN KEY (current_version_id)
                        REFERENCES documents.document_versions (id) DEFERRABLE INITIALLY DEFERRED,
                    ADD CONSTRAINT fk_documents_effective_version FOREIGN KEY (effective_version_id)
                        REFERENCES documents.document_versions (id) DEFERRABLE INITIALLY DEFERRED;
                """);

            // One tree, one root (section 5.2): a second parentless category is refused.
            migrationBuilder.Sql("""
                CREATE UNIQUE INDEX ux_categories_single_root ON documents.categories ((true))
                    WHERE parent_id IS NULL;
                """);

            // The short, explicit list of integrity-critical foreign keys across modules
            // (section 2.4). Storage and document types migrate before this module.
            migrationBuilder.Sql("""
                ALTER TABLE documents.document_versions
                    ADD CONSTRAINT fk_document_versions_storage_object FOREIGN KEY (storage_object_id)
                        REFERENCES storage.storage_objects (id),
                    ADD CONSTRAINT fk_document_versions_type_version FOREIGN KEY (document_type_version_id)
                        REFERENCES doctypes.document_type_versions (id);

                ALTER TABLE documents.documents
                    ADD CONSTRAINT fk_documents_document_type FOREIGN KEY (document_type_id)
                        REFERENCES doctypes.document_types (id);
                """);

            // Versions are immutable (ADR 0001). Only the approval state may change, which the
            // workflow phase drives, and rows may only be deleted by the purge routine, which
            // switches dms.purge on for its own transaction. This does not rely on application
            // discipline: a raw UPDATE from any client is refused just the same.
            migrationBuilder.Sql("""
                CREATE FUNCTION documents.protect_document_versions() RETURNS trigger
                LANGUAGE plpgsql AS $$
                BEGIN
                    IF TG_OP = 'DELETE' THEN
                        IF coalesce(current_setting('dms.purge', true), '') = 'on' THEN
                            RETURN OLD;
                        END IF;
                        RAISE EXCEPTION 'document versions can only be removed by a purge'
                            USING ERRCODE = 'integrity_constraint_violation';
                    END IF;

                    IF (NEW.id, NEW.document_id, NEW.version_number, NEW.revision_number,
                        NEW.storage_object_id, NEW.file_name, NEW.mime_type, NEW.file_size, NEW.sha256,
                        NEW.document_type_version_id, NEW.dynamic_data, NEW.change_kind,
                        NEW.change_description, NEW.created_by, NEW.created_at)
                       IS DISTINCT FROM
                       (OLD.id, OLD.document_id, OLD.version_number, OLD.revision_number,
                        OLD.storage_object_id, OLD.file_name, OLD.mime_type, OLD.file_size, OLD.sha256,
                        OLD.document_type_version_id, OLD.dynamic_data, OLD.change_kind,
                        OLD.change_description, OLD.created_by, OLD.created_at) THEN
                        RAISE EXCEPTION 'document versions are immutable; only approval_status and approved_at may change'
                            USING ERRCODE = 'integrity_constraint_violation';
                    END IF;

                    RETURN NEW;
                END;
                $$;

                CREATE TRIGGER trg_document_versions_immutable
                    BEFORE UPDATE OR DELETE ON documents.document_versions
                    FOR EACH ROW EXECUTE FUNCTION documents.protect_document_versions();

                -- TRUNCATE bypasses row triggers, so it is refused outright.
                CREATE FUNCTION documents.refuse_truncate() RETURNS trigger
                LANGUAGE plpgsql AS $$
                BEGIN
                    RAISE EXCEPTION 'document versions cannot be truncated';
                END;
                $$;

                CREATE TRIGGER trg_document_versions_no_truncate
                    BEFORE TRUNCATE ON documents.document_versions
                    FOR EACH STATEMENT EXECUTE FUNCTION documents.refuse_truncate();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS trg_document_versions_no_truncate ON documents.document_versions;
                DROP TRIGGER IF EXISTS trg_document_versions_immutable ON documents.document_versions;
                DROP FUNCTION IF EXISTS documents.refuse_truncate();
                DROP FUNCTION IF EXISTS documents.protect_document_versions();
                ALTER TABLE documents.documents
                    DROP CONSTRAINT IF EXISTS fk_documents_current_version,
                    DROP CONSTRAINT IF EXISTS fk_documents_effective_version;
                """);

            migrationBuilder.DropTable(
                name: "document_tags",
                schema: "documents");

            migrationBuilder.DropTable(
                name: "document_versions",
                schema: "documents");

            migrationBuilder.DropTable(
                name: "tags",
                schema: "documents");

            migrationBuilder.DropTable(
                name: "documents",
                schema: "documents");

            migrationBuilder.DropTable(
                name: "categories",
                schema: "documents");
        }
    }
}
