using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Dms.Audit.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Phase 7: the seal chain (append-only like the log), TRUNCATE refused on the log, its
    /// partitions and the seals, and partition creation that works for the runtime role, which
    /// has no DDL rights (section 4.10).
    /// </summary>
    public partial class AuditSealsAndHardening : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "audit_seals",
                schema: "audit",
                columns: table => new
                {
                    sequence = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    period_start = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    period_end = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    row_count = table.Column<long>(type: "bigint", nullable: false),
                    rows_digest = table.Column<byte[]>(type: "bytea", nullable: false),
                    previous_hash = table.Column<byte[]>(type: "bytea", nullable: true),
                    seal_hash = table.Column<byte[]>(type: "bytea", nullable: false),
                    algorithm = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    key_id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    sealed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audit_seals", x => x.sequence);
                });

            migrationBuilder.CreateIndex(
                name: "ux_audit_seals_period_start",
                schema: "audit",
                table: "audit_seals",
                column: "period_start",
                unique: true);

            migrationBuilder.Sql("""
                ALTER TABLE audit.audit_seals
                    ADD CONSTRAINT ck_audit_seals_period CHECK (period_end > period_start),
                    ADD CONSTRAINT ck_audit_seals_row_count CHECK (row_count >= 0),
                    ADD CONSTRAINT ck_audit_seals_algorithm CHECK (algorithm IN ('HMAC-SHA256', 'SHA-256'));

                CREATE TRIGGER trg_audit_seals_append_only
                    BEFORE UPDATE OR DELETE ON audit.audit_seals
                    FOR EACH ROW EXECUTE FUNCTION audit.reject_mutation();
                CREATE TRIGGER trg_audit_seals_no_truncate
                    BEFORE TRUNCATE ON audit.audit_seals
                    FOR EACH STATEMENT EXECUTE FUNCTION audit.reject_mutation();
                """);

            // A TRUNCATE trigger on a partitioned table fires only for TRUNCATE of the parent, so
            // every partition gets its own, now and whenever ensure_partition creates one.
            migrationBuilder.Sql("""
                CREATE TRIGGER trg_audit_logs_no_truncate
                    BEFORE TRUNCATE ON audit.audit_logs
                    FOR EACH STATEMENT EXECUTE FUNCTION audit.reject_mutation();

                DO $$
                DECLARE
                    part regclass;
                BEGIN
                    FOR part IN SELECT inhrelid::regclass FROM pg_inherits WHERE inhparent = 'audit.audit_logs'::regclass LOOP
                        EXECUTE format(
                            'CREATE TRIGGER trg_no_truncate BEFORE TRUNCATE ON %s FOR EACH STATEMENT EXECUTE FUNCTION audit.reject_mutation()',
                            part);
                    END LOOP;
                END
                $$;
                """);

            // SECURITY DEFINER: the maintenance job runs as the runtime role, which may not create
            // tables. The function can only ever add next months' partitions of this one table.
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION audit.ensure_partition(p_month date) RETURNS void
                LANGUAGE plpgsql
                SECURITY DEFINER
                SET search_path = pg_catalog, audit
                AS $$
                DECLARE
                    start_date date := date_trunc('month', p_month)::date;
                    end_date   date := (date_trunc('month', p_month) + interval '1 month')::date;
                    part_name  text := format('audit_logs_%s', to_char(date_trunc('month', p_month), 'YYYY_MM'));
                BEGIN
                    IF NOT EXISTS (
                        SELECT 1 FROM pg_class c
                        JOIN pg_namespace n ON n.oid = c.relnamespace
                        WHERE n.nspname = 'audit' AND c.relname = part_name)
                    THEN
                        EXECUTE format(
                            'CREATE TABLE audit.%I PARTITION OF audit.audit_logs FOR VALUES FROM (%L) TO (%L)',
                            part_name, start_date, end_date);
                        EXECUTE format(
                            'CREATE TRIGGER trg_no_truncate BEFORE TRUNCATE ON audit.%I FOR EACH STATEMENT EXECUTE FUNCTION audit.reject_mutation()',
                            part_name);
                    END IF;
                END;
                $$;

                REVOKE ALL ON FUNCTION audit.ensure_partition(date) FROM PUBLIC;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_audit_logs_no_truncate ON audit.audit_logs;");
            migrationBuilder.DropTable(
                name: "audit_seals",
                schema: "audit");
        }
    }
}
