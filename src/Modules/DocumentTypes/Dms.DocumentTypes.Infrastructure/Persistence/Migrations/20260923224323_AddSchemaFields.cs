using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dms.DocumentTypes.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSchemaFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "field_definitions",
                schema: "doctypes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    type_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(63)", maxLength: 63, nullable: false),
                    label = table.Column<string>(type: "jsonb", nullable: false),
                    field_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    is_required = table.Column<bool>(type: "boolean", nullable: false),
                    is_searchable = table.Column<bool>(type: "boolean", nullable: false),
                    is_sortable = table.Column<bool>(type: "boolean", nullable: false),
                    show_in_list = table.Column<bool>(type: "boolean", nullable: false),
                    is_approval_relevant = table.Column<bool>(type: "boolean", nullable: false),
                    default_value = table.Column<string>(type: "jsonb", nullable: true),
                    validation = table.Column<string>(type: "jsonb", nullable: false),
                    help_text = table.Column<string>(type: "jsonb", nullable: true),
                    display_order = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_field_definitions", x => x.id);
                    table.CheckConstraint("ck_field_definitions_code", "code ~ '^[a-z][a-z0-9_]{0,62}$'");
                    table.ForeignKey(
                        name: "fk_field_definitions_version",
                        column: x => x.type_version_id,
                        principalSchema: "doctypes",
                        principalTable: "document_type_versions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "field_rules",
                schema: "doctypes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    type_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    condition = table.Column<string>(type: "jsonb", nullable: true),
                    target_field_codes = table.Column<string[]>(type: "text[]", nullable: false),
                    assertion = table.Column<string>(type: "jsonb", nullable: true),
                    message = table.Column<string>(type: "jsonb", nullable: true),
                    display_order = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_field_rules", x => x.id);
                    table.ForeignKey(
                        name: "fk_field_rules_version",
                        column: x => x.type_version_id,
                        principalSchema: "doctypes",
                        principalTable: "document_type_versions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "field_options",
                schema: "doctypes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    field_definition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    value = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    label = table.Column<string>(type: "jsonb", nullable: false),
                    display_order = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_field_options", x => x.id);
                    table.ForeignKey(
                        name: "fk_field_options_field",
                        column: x => x.field_definition_id,
                        principalSchema: "doctypes",
                        principalTable: "field_definitions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ux_field_definitions_code",
                schema: "doctypes",
                table: "field_definitions",
                columns: new[] { "type_version_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_field_options_value",
                schema: "doctypes",
                table: "field_options",
                columns: new[] { "field_definition_id", "value" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_field_rules_version",
                schema: "doctypes",
                table: "field_rules",
                column: "type_version_id");

            // Published schemas are immutable (section 4.5), in the database and not only in the
            // aggregate: fields, options and rules under a non-draft version cannot be inserted,
            // changed or deleted, and a published version cannot go back to draft or disappear.
            migrationBuilder.Sql("""
                CREATE FUNCTION doctypes.protect_published_schema() RETURNS trigger
                LANGUAGE plpgsql AS $$
                DECLARE
                    affected record;
                    owning_version uuid;
                    version_status text;
                BEGIN
                    IF TG_OP = 'DELETE' THEN affected := OLD; ELSE affected := NEW; END IF;

                    IF TG_TABLE_NAME = 'field_options' THEN
                        SELECT type_version_id INTO owning_version
                          FROM doctypes.field_definitions WHERE id = affected.field_definition_id;
                    ELSE
                        owning_version := affected.type_version_id;
                    END IF;

                    SELECT status INTO version_status
                      FROM doctypes.document_type_versions WHERE id = owning_version;

                    IF version_status IS NOT NULL AND version_status <> 'Draft' THEN
                        RAISE EXCEPTION 'published document type schemas are immutable'
                            USING ERRCODE = 'integrity_constraint_violation';
                    END IF;

                    RETURN affected;
                END;
                $$;

                CREATE TRIGGER trg_field_definitions_immutable
                    BEFORE INSERT OR UPDATE OR DELETE ON doctypes.field_definitions
                    FOR EACH ROW EXECUTE FUNCTION doctypes.protect_published_schema();
                CREATE TRIGGER trg_field_options_immutable
                    BEFORE INSERT OR UPDATE OR DELETE ON doctypes.field_options
                    FOR EACH ROW EXECUTE FUNCTION doctypes.protect_published_schema();
                CREATE TRIGGER trg_field_rules_immutable
                    BEFORE INSERT OR UPDATE OR DELETE ON doctypes.field_rules
                    FOR EACH ROW EXECUTE FUNCTION doctypes.protect_published_schema();

                CREATE FUNCTION doctypes.protect_published_versions() RETURNS trigger
                LANGUAGE plpgsql AS $$
                BEGIN
                    IF OLD.status = 'Draft' THEN
                        RETURN COALESCE(NEW, OLD);
                    END IF;

                    IF TG_OP = 'DELETE'
                       OR NEW.status = 'Draft'
                       OR NEW.document_type_id <> OLD.document_type_id
                       OR NEW.version_number <> OLD.version_number
                       OR NEW.published_at IS DISTINCT FROM OLD.published_at THEN
                        RAISE EXCEPTION 'a published document type version cannot be changed or removed'
                            USING ERRCODE = 'integrity_constraint_violation';
                    END IF;

                    RETURN NEW;
                END;
                $$;

                CREATE TRIGGER trg_document_type_versions_immutable
                    BEFORE UPDATE OR DELETE ON doctypes.document_type_versions
                    FOR EACH ROW EXECUTE FUNCTION doctypes.protect_published_versions();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS trg_document_type_versions_immutable ON doctypes.document_type_versions;
                DROP FUNCTION IF EXISTS doctypes.protect_published_versions();
                DROP TRIGGER IF EXISTS trg_field_rules_immutable ON doctypes.field_rules;
                DROP TRIGGER IF EXISTS trg_field_options_immutable ON doctypes.field_options;
                DROP TRIGGER IF EXISTS trg_field_definitions_immutable ON doctypes.field_definitions;
                DROP FUNCTION IF EXISTS doctypes.protect_published_schema();
                """);

            migrationBuilder.DropTable(
                name: "field_options",
                schema: "doctypes");

            migrationBuilder.DropTable(
                name: "field_rules",
                schema: "doctypes");

            migrationBuilder.DropTable(
                name: "field_definitions",
                schema: "doctypes");
        }
    }
}
