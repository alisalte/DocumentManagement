using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dms.Audit.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// The audit table is written by hand rather than generated, because it needs three things EF
    /// cannot express:
    ///   * RANGE partitioning by month (audit is the highest volume table in the system, and
    ///     retention should be a matter of detaching a partition, not a mass delete);
    ///   * an append-only trigger, so the log cannot be rewritten even by a compromised app role;
    ///   * grants that give the runtime role INSERT and SELECT but never UPDATE or DELETE.
    /// The EF model still matches the resulting shape, so queries and the snapshot stay valid.
    /// </summary>
    public partial class InitialAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(name: "audit");

            migrationBuilder.Sql("""
                CREATE TABLE audit.audit_logs (
                    id              uuid          NOT NULL,
                    occurred_at     timestamptz   NOT NULL,
                    actor_type      varchar(16)   NOT NULL,
                    user_id         uuid          NULL,
                    share_link_id   uuid          NULL,
                    action          varchar(64)   NOT NULL,
                    outcome         varchar(16)   NOT NULL,
                    entity_type     varchar(64)   NULL,
                    entity_id       uuid          NULL,
                    document_id     uuid          NULL,
                    version_id      uuid          NULL,
                    ip_address      varchar(64)   NULL,
                    user_agent      varchar(512)  NULL,
                    correlation_id  varchar(64)   NULL,
                    metadata        jsonb         NOT NULL,
                    CONSTRAINT "PK_audit_logs" PRIMARY KEY (id, occurred_at),
                    CONSTRAINT ck_audit_outcome CHECK (outcome IN ('SUCCESS','DENIED','FAILED')),
                    CONSTRAINT ck_audit_actor_type
                        CHECK (actor_type IN ('USER','SHARELINK','SYSTEM','ANONYMOUS'))
                ) PARTITION BY RANGE (occurred_at);
                """);

            migrationBuilder.Sql("""
                CREATE INDEX ix_audit_document ON audit.audit_logs (document_id, occurred_at DESC);
                CREATE INDEX ix_audit_user     ON audit.audit_logs (user_id, occurred_at DESC);
                CREATE INDEX ix_audit_action   ON audit.audit_logs (action, occurred_at DESC);
                CREATE INDEX ix_audit_occurred ON audit.audit_logs USING brin (occurred_at);
                """);

            migrationBuilder.Sql("""
                CREATE FUNCTION audit.ensure_partition(p_month date) RETURNS void
                LANGUAGE plpgsql AS $$
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
                    END IF;
                END;
                $$;
                """);

            // A default partition means an insert can never fail because a partition is missing,
            // even if the maintenance job has not run.
            migrationBuilder.Sql(
                "CREATE TABLE audit.audit_logs_default PARTITION OF audit.audit_logs DEFAULT;");

            migrationBuilder.Sql("""
                SELECT audit.ensure_partition(date_trunc('month', now())::date);
                SELECT audit.ensure_partition((date_trunc('month', now()) + interval '1 month')::date);
                SELECT audit.ensure_partition((date_trunc('month', now()) + interval '2 month')::date);
                """);

            migrationBuilder.Sql("""
                CREATE FUNCTION audit.reject_mutation() RETURNS trigger
                LANGUAGE plpgsql AS $$
                BEGIN
                    RAISE EXCEPTION 'audit.audit_logs is append-only (attempted %)', TG_OP
                        USING ERRCODE = '42501';
                END;
                $$;

                CREATE TRIGGER trg_audit_logs_append_only
                    BEFORE UPDATE OR DELETE ON audit.audit_logs
                    FOR EACH ROW EXECUTE FUNCTION audit.reject_mutation();
                """);

            // The runtime role is created by the deployment, not by the migration, so the grants
            // only apply when it exists.
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'dms_app') THEN
                        EXECUTE 'GRANT USAGE ON SCHEMA audit TO dms_app';
                        EXECUTE 'GRANT INSERT, SELECT ON ALL TABLES IN SCHEMA audit TO dms_app';
                        EXECUTE 'REVOKE UPDATE, DELETE, TRUNCATE ON ALL TABLES IN SCHEMA audit FROM dms_app';
                        EXECUTE 'ALTER DEFAULT PRIVILEGES IN SCHEMA audit '
                             || 'GRANT INSERT, SELECT ON TABLES TO dms_app';
                    END IF;
                END
                $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TABLE IF EXISTS audit.audit_logs CASCADE;");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS audit.ensure_partition(date);");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS audit.reject_mutation();");
        }
    }
}
