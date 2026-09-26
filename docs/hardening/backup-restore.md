# Backup and restore drill

The archive is two stores: **PostgreSQL** (metadata, ACL, audit, jobs) and **object storage**
(filesystem volume or S3/MinIO). A backup that only dumps the database cannot restore files.

## What to back up

| Component | Compose | Dev (`dev-db.sh`) |
|---|---|---|
| PostgreSQL | volume `postgres-data` | volume `dms-pgdata-v18` |
| Objects | volume `object-data` | `.data/objects` under the API working directory |
| Secrets | `.env` / secret store — **not** inside the DB dump | same |

Keep `Dms__Audit__SealKey` with the backups: without it, seal verification of older periods fails
even when the rows are intact.

## Automated drill

```bash
# Docker container from scripts/dev-db.sh (default), or a local Postgres:
#   DMS_DRILL_MODE=local PGHOST=localhost PGPORT=5432 ./scripts/backup-restore-drill.sh
./scripts/backup-restore-drill.sh
```

The script auto-selects `docker` when `dms-postgres` is running, otherwise `local` when
`pg_isready` succeeds.

1. Creates a throwaway database and a marker table row (or uses `DMS_DRILL_DATABASE`).
2. `pg_dump` → temporary directory.
3. Drops and recreates the database.
4. `pg_restore` / `psql` applies the dump.
5. Checks the marker row is present.
6. Cleans up.

Exit code 0 means the DB half of the drill passed. Object-store restore is checked when
`DMS_DRILL_OBJECTS` points at a directory (copies out and back a sentinel file).

## Production cadence (recommended)

- Daily logical dump (`pg_dump -Fc`) retained ≥ 14 days
- Weekly volume snapshot of object storage
- Quarterly full restore into an isolated environment, then run `scripts/test.sh` smoke subset
  and `POST /api/v1/audit/seals/verify` for the last 30 days
