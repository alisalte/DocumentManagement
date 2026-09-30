-- Examples: pull metadata + binary content out of a legacy SQL DMS.
-- The importer does NOT connect to the old database. Export BLOBs to files first
-- (see scripts/legacy-export-sql-blobs.py), then import with the JSON manifest.

-- ---------------------------------------------------------------------------
-- SQL Server: IMAGE / VARBINARY(MAX)
-- ---------------------------------------------------------------------------
-- Adjust table/column names to the legacy schema. Export as JSON for the script:
--
--   sqlcmd -S . -d OldDms -E -y 0 -Q "SET NOCOUNT ON; ..." -o legacy-rows.json
--
-- Or use the Python script with --sql / --dsn (pyodbc):
--
--   python3 scripts/legacy-export-sql-blobs.py \
--     --dsn "Driver={ODBC Driver 18 for SQL Server};Server=...;Database=OldDms;Trusted_Connection=yes;TrustServerCertificate=yes;" \
--     --sql "$(cat docs/legacy-import/sql-blobs.example.sql | sed -n '/SQL Server SELECT/,/SQL Server END/p')" \
--     --out-dir /var/lib/dms/import-staging/legacy \
--     --source old-sqlserver

-- SQL Server SELECT
SELECT
    CAST(d.DocId AS nvarchar(64))          AS sourceId,
    d.Title                                AS title,
    ISNULL(d.FolderPath, N'واردات')        AS categoryPath,
    ISNULL(d.DocTypeCode, N'GENERAL')      AS documentTypeCode,
    ISNULL(d.OriginalFileName, N'file.bin') AS fileName,
    d.MimeType                             AS contentType,
    d.Body                                 AS content,          -- VARBINARY(MAX) / IMAGE
    d.CreatedBy                            AS ownerUsername,
    CONVERT(varchar(33), d.CreatedAt, 127) AS createdAt
FROM dbo.Documents AS d
WHERE d.Body IS NOT NULL;
-- SQL Server END


-- ---------------------------------------------------------------------------
-- PostgreSQL: bytea
-- ---------------------------------------------------------------------------
-- SQL Postgres SELECT
SELECT
    d.id::text                    AS "sourceId",
    d.title                       AS title,
    COALESCE(d.folder_path, 'واردات') AS "categoryPath",
    COALESCE(d.type_code, 'GENERAL')  AS "documentTypeCode",
    COALESCE(d.file_name, 'file.bin') AS "fileName",
    d.mime_type                   AS "contentType",
    d.content                     AS content,                 -- bytea
    d.owner_username              AS "ownerUsername",
    d.created_at                  AS "createdAt"
FROM legacy.documents AS d
WHERE d.content IS NOT NULL;
-- SQL Postgres END


-- ---------------------------------------------------------------------------
-- Offline path (no live DB driver): dump to JSONL with base64
-- ---------------------------------------------------------------------------
-- Example row written to legacy-rows.jsonl (one object per line):
--
-- {"sourceId":"42","title":"قرارداد","categoryPath":"قراردادها/۱۴۰۴","documentTypeCode":"GENERAL","fileName":"a.pdf","contentBase64":"JVBERi0x..."}
--
-- Then:
--   python3 scripts/legacy-export-sql-blobs.py \
--     --rows legacy-rows.jsonl \
--     --out-dir /var/lib/dms/import-staging/legacy \
--     --source old-sql-dms
