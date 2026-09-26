# Go-live runbook

Use this against a staging stack that mirrors production before the first cutover.

## Pre-flight

1. Secrets in the environment (never in the image or the database dump):
   - `DMS_JWT_SIGNING_KEY` (≥ 32 bytes)
   - `DMS_DB_APP_PASSWORD` (role `dms_app` only; migrator uses the owner)
   - `DMS_ADMIN_PASSWORD` (bootstrap once; force change at first sign-in)
   - `DMS_AUDIT_SEAL_KEY` (keep with offline backups, not inside Postgres dumps)
2. `ASPNETCORE_ENVIRONMENT=Production` — OpenAPI / Scalar must not be mapped.
3. `Dms:Cors:Origins` (or compose `DMS_FRONTEND_ORIGIN`) is the real HTTPS origin only.
4. TLS terminates at the gateway; confirm `Strict-Transport-Security` on responses (phase 9 headers).
5. Postgres role: API/worker connect as `dms_app`; confirm audit append-only (phase 7).
6. Object storage volume or S3 bucket is writable and backed up with the database.
7. Optional profiles sized from D11 business answers: `search`, `scan`, `office`.

## Cutover steps

1. Take a cold backup (see [../hardening/backup-restore.md](../hardening/backup-restore.md)).
2. `docker compose up -d --build` (or your orchestrator) with Production env.
3. Wait for migrator exit 0; confirm `/health/ready` and `/version`.
4. Sign in as bootstrap admin → change password → create real admins → deactivate bootstrap if policy requires.
5. Seed categories, document types, roles and ACLs for the pilot department.
6. Run `scripts/load-test.sh` and `scripts/backup-restore-drill.sh` against staging.
7. Walk [../hardening/pen-test-checklist.md](../hardening/pen-test-checklist.md); keep a signed copy.
8. Open the pilot; watch `/health/*`, job failures, and audit seal verification.

## Rollback

1. Stop API and worker.
2. Restore Postgres from the pre-cutover dump; restore object storage to the matching snapshot.
3. Redeploy the previous image tags.
4. Verify seal key still matches the restored audit chain.
