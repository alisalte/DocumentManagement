using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dms.Documents.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AllowInPlaceMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION documents.protect_document_versions() RETURNS trigger
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
                        NEW.document_type_version_id, NEW.change_kind,
                        NEW.change_description, NEW.created_by, NEW.created_at)
                       IS DISTINCT FROM
                       (OLD.id, OLD.document_id, OLD.version_number, OLD.revision_number,
                        OLD.storage_object_id, OLD.file_name, OLD.mime_type, OLD.file_size, OLD.sha256,
                        OLD.document_type_version_id, OLD.change_kind,
                        OLD.change_description, OLD.created_by, OLD.created_at) THEN
                        RAISE EXCEPTION 'document versions are immutable; only approval_status and approved_at may change'
                            USING ERRCODE = 'integrity_constraint_violation';
                    END IF;

                    -- MetadataEditPolicy.InPlace (ADR 0001): only inside a transaction that declared
                    -- it, and the application writes the before/after diff to the audit log.
                    IF NEW.dynamic_data IS DISTINCT FROM OLD.dynamic_data
                       AND coalesce(current_setting('dms.metadata_in_place', true), '') <> 'on' THEN
                        RAISE EXCEPTION 'document versions are immutable; metadata may only change in place when the document type allows it'
                            USING ERRCODE = 'integrity_constraint_violation';
                    END IF;

                    RETURN NEW;
                END;
                $$;
                """);

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION documents.protect_document_versions() RETURNS trigger
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
                        NEW.document_type_version_id, NEW.change_kind,
                        NEW.change_description, NEW.created_by, NEW.created_at)
                       IS DISTINCT FROM
                       (OLD.id, OLD.document_id, OLD.version_number, OLD.revision_number,
                        OLD.storage_object_id, OLD.file_name, OLD.mime_type, OLD.file_size, OLD.sha256,
                        OLD.document_type_version_id, OLD.change_kind,
                        OLD.change_description, OLD.created_by, OLD.created_at) THEN
                        RAISE EXCEPTION 'document versions are immutable; only approval_status and approved_at may change'
                            USING ERRCODE = 'integrity_constraint_violation';
                    END IF;

                    IF NEW.dynamic_data IS DISTINCT FROM OLD.dynamic_data THEN
                        RAISE EXCEPTION 'document versions are immutable; only approval_status and approved_at may change'
                            USING ERRCODE = 'integrity_constraint_violation';
                    END IF;

                    RETURN NEW;
                END;
                $$;
                """);

        }
    }
}
