using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dms.Workflow.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialWorkflow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "workflow");

            migrationBuilder.CreateTable(
                name: "workflows",
                schema: "workflow",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    latest_published_version_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_workflows", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "workflow_versions",
                schema: "workflow",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    workflow_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version_number = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    published_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_workflow_versions", x => x.id);
                    table.ForeignKey(
                        name: "FK_workflow_versions_workflows_workflow_id",
                        column: x => x.workflow_id,
                        principalSchema: "workflow",
                        principalTable: "workflows",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "workflow_instances",
                schema: "workflow",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version_sort_key = table.Column<long>(type: "bigint", nullable: false),
                    workflow_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    current_sequence = table.Column<int>(type: "integer", nullable: false),
                    round = table.Column<int>(type: "integer", nullable: false),
                    started_by = table.Column<Guid>(type: "uuid", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cancel_reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    attention_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    skipped_steps = table.Column<List<string>>(type: "text[]", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_workflow_instances", x => x.id);
                    table.ForeignKey(
                        name: "fk_workflow_instances_workflow_version",
                        column: x => x.workflow_version_id,
                        principalSchema: "workflow",
                        principalTable: "workflow_versions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "workflow_steps",
                schema: "workflow",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    workflow_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(63)", maxLength: 63, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    sequence = table.Column<int>(type: "integer", nullable: false),
                    assignee_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    assignee_id = table.Column<Guid>(type: "uuid", nullable: true),
                    assignee_field_code = table.Column<string>(type: "character varying(63)", maxLength: 63, nullable: true),
                    completion_rule = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    is_required = table.Column<bool>(type: "boolean", nullable: false),
                    sla_hours = table.Column<int>(type: "integer", nullable: true),
                    allow_self_approval = table.Column<bool>(type: "boolean", nullable: false),
                    condition = table.Column<string>(type: "jsonb", nullable: true),
                    actions = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_workflow_steps", x => x.id);
                    table.CheckConstraint("ck_workflow_steps_assignee", "(assignee_type IN ('User','Group','Role')) = (assignee_id IS NOT NULL) AND (assignee_type = 'DynamicUserField') = (assignee_field_code IS NOT NULL)");
                    table.CheckConstraint("ck_workflow_steps_sequence", "sequence BETWEEN 1 AND 1000");
                    table.ForeignKey(
                        name: "fk_workflow_steps_version",
                        column: x => x.workflow_version_id,
                        principalSchema: "workflow",
                        principalTable: "workflow_versions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "workflow_tasks",
                schema: "workflow",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    instance_id = table.Column<Guid>(type: "uuid", nullable: false),
                    step_code = table.Column<string>(type: "character varying(63)", maxLength: 63, nullable: false),
                    sequence = table.Column<int>(type: "integer", nullable: false),
                    round = table.Column<int>(type: "integer", nullable: false),
                    assigned_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    assigned_group_id = table.Column<Guid>(type: "uuid", nullable: true),
                    assigned_role_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    due_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    overdue_notified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    completed_by = table.Column<Guid>(type: "uuid", nullable: true),
                    action = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: true),
                    comment = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    forwarded_from_task_id = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_workflow_tasks", x => x.id);
                    table.CheckConstraint("ck_workflow_tasks_one_assignee", "num_nonnulls(assigned_user_id, assigned_group_id, assigned_role_id) = 1");
                    table.ForeignKey(
                        name: "fk_workflow_tasks_instance",
                        column: x => x.instance_id,
                        principalSchema: "workflow",
                        principalTable: "workflow_instances",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_workflow_instances_document",
                schema: "workflow",
                table: "workflow_instances",
                column: "document_id");

            migrationBuilder.CreateIndex(
                name: "ix_workflow_instances_status",
                schema: "workflow",
                table: "workflow_instances",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "IX_workflow_instances_workflow_version_id",
                schema: "workflow",
                table: "workflow_instances",
                column: "workflow_version_id");

            migrationBuilder.CreateIndex(
                name: "ux_workflow_instances_running_version",
                schema: "workflow",
                table: "workflow_instances",
                column: "document_version_id",
                unique: true,
                filter: "status = 'Running'");

            migrationBuilder.CreateIndex(
                name: "ix_workflow_steps_sequence",
                schema: "workflow",
                table: "workflow_steps",
                columns: new[] { "workflow_version_id", "sequence" });

            migrationBuilder.CreateIndex(
                name: "ux_workflow_steps_code",
                schema: "workflow",
                table: "workflow_steps",
                columns: new[] { "workflow_version_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_workflow_tasks_due",
                schema: "workflow",
                table: "workflow_tasks",
                column: "due_at",
                filter: "status = 'Pending'");

            migrationBuilder.CreateIndex(
                name: "ix_workflow_tasks_group",
                schema: "workflow",
                table: "workflow_tasks",
                column: "assigned_group_id",
                filter: "status = 'Pending'");

            migrationBuilder.CreateIndex(
                name: "ix_workflow_tasks_instance",
                schema: "workflow",
                table: "workflow_tasks",
                column: "instance_id");

            migrationBuilder.CreateIndex(
                name: "ix_workflow_tasks_role",
                schema: "workflow",
                table: "workflow_tasks",
                column: "assigned_role_id",
                filter: "status = 'Pending'");

            migrationBuilder.CreateIndex(
                name: "ix_workflow_tasks_user",
                schema: "workflow",
                table: "workflow_tasks",
                column: "assigned_user_id",
                filter: "status = 'Pending'");

            migrationBuilder.CreateIndex(
                name: "ux_workflow_versions_number",
                schema: "workflow",
                table: "workflow_versions",
                columns: new[] { "workflow_id", "version_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_workflow_versions_single_draft",
                schema: "workflow",
                table: "workflow_versions",
                column: "workflow_id",
                unique: true,
                filter: "status = 'Draft'");

            migrationBuilder.CreateIndex(
                name: "ux_workflows_code",
                schema: "workflow",
                table: "workflows",
                column: "code",
                unique: true);
            // An instance belongs to one document version. Deferred, because an AUTO_ON_VERSION
            // start inserts the instance in the same transaction as the version it reviews.
            migrationBuilder.Sql("""
                ALTER TABLE workflow.workflow_instances
                    ADD CONSTRAINT fk_workflow_instances_document_version FOREIGN KEY (document_version_id)
                        REFERENCES documents.document_versions (id) DEFERRABLE INITIALLY DEFERRED;
                """);

            // Published workflow versions and their steps never change (section 6.1), and running
            // instances rely on that: they stay pinned to the steps they started with.
            migrationBuilder.Sql("""
                CREATE FUNCTION workflow.protect_published_steps() RETURNS trigger
                LANGUAGE plpgsql AS $$
                DECLARE
                    affected record;
                    version_status text;
                BEGIN
                    IF TG_OP = 'DELETE' THEN affected := OLD; ELSE affected := NEW; END IF;
                    SELECT status INTO version_status FROM workflow.workflow_versions WHERE id = affected.workflow_version_id;
                    IF version_status IS NOT NULL AND version_status <> 'Draft' THEN
                        RAISE EXCEPTION 'published workflow versions are immutable'
                            USING ERRCODE = 'integrity_constraint_violation';
                    END IF;
                    RETURN affected;
                END;
                $$;

                CREATE TRIGGER trg_workflow_steps_immutable
                    BEFORE INSERT OR UPDATE OR DELETE ON workflow.workflow_steps
                    FOR EACH ROW EXECUTE FUNCTION workflow.protect_published_steps();

                CREATE FUNCTION workflow.protect_published_versions() RETURNS trigger
                LANGUAGE plpgsql AS $$
                BEGIN
                    IF OLD.status = 'Draft' THEN
                        RETURN COALESCE(NEW, OLD);
                    END IF;
                    IF TG_OP = 'DELETE'
                       OR NEW.status = 'Draft'
                       OR NEW.workflow_id <> OLD.workflow_id
                       OR NEW.version_number <> OLD.version_number
                       OR NEW.published_at IS DISTINCT FROM OLD.published_at THEN
                        RAISE EXCEPTION 'a published workflow version cannot be changed or removed'
                            USING ERRCODE = 'integrity_constraint_violation';
                    END IF;
                    RETURN NEW;
                END;
                $$;

                CREATE TRIGGER trg_workflow_versions_immutable
                    BEFORE UPDATE OR DELETE ON workflow.workflow_versions
                    FOR EACH ROW EXECUTE FUNCTION workflow.protect_published_versions();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS trg_workflow_versions_immutable ON workflow.workflow_versions;
                DROP FUNCTION IF EXISTS workflow.protect_published_versions();
                DROP TRIGGER IF EXISTS trg_workflow_steps_immutable ON workflow.workflow_steps;
                DROP FUNCTION IF EXISTS workflow.protect_published_steps();
                ALTER TABLE workflow.workflow_instances DROP CONSTRAINT IF EXISTS fk_workflow_instances_document_version;
                """);

            migrationBuilder.DropTable(
                name: "workflow_steps",
                schema: "workflow");

            migrationBuilder.DropTable(
                name: "workflow_tasks",
                schema: "workflow");

            migrationBuilder.DropTable(
                name: "workflow_instances",
                schema: "workflow");

            migrationBuilder.DropTable(
                name: "workflow_versions",
                schema: "workflow");

            migrationBuilder.DropTable(
                name: "workflows",
                schema: "workflow");
        }
    }
}
