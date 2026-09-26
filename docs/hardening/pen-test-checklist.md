# Pen-test checklist (phase 9)

Manual / tool-assisted review before go-live. Mark each row when verified against a staging
deployment that mirrors production (TLS, `dms_app` role, seal key, CORS locked to the real origin).

Automated coverage column points at the regression that already locks the behaviour in CI; still
re-check on staging for environment-only items (TLS, CORS origin, secrets in images).

## Authentication and sessions

| Check | Auto |
|---|---|
| [x] Wrong password and unknown username return the same status and body shape (no account enumeration) | `AuthenticationApiTests` |
| [x] Refresh-token reuse is refused and audited (`TOKEN_REUSE_DETECTED`) | `AuthenticationApiTests` |
| [x] Forced password change blocks every API except change-password / sign-out | `AdministrationApiTests` |
| [x] Login rate limit returns HTTP 429 after `Dms:RateLimits:LoginPerMinute` (default 10) | k6 `load/k6/login-abuse.js` (verified locally) |
| [x] JWT without a valid signature is rejected; expired tokens are rejected | `SecuritySuiteTests` / `AuthenticationApiTests` |
| [x] Deactivating a user clears effective permissions on the next authorized call | `AdministrationApiTests` |

## Authorization

| Check | Auto |
|---|---|
| [x] Direct document URL for a document the caller cannot see → 404 (not 403 with a title) | `SecuritySuiteTests` |
| [x] Search never returns titles/snippets/facets for documents outside the caller's scope | `SecuritySuiteTests` / `SearchApiTests` |
| [x] VIEW without DOWNLOAD cannot fetch the original file; PRINT uses watermarked renditions only | `SecuritySuiteTests` / `DocumentApiTests` / preview suite |
| [x] Explicit DENY beats every ALLOW, including inherited and more specific allows | Authorization unit + API tests |
| [x] `ADMIN_MANAGE_USERS` cannot promote to system admin or touch an existing administrator | `AdministrationApiTests` |
| [x] Role managers cannot assign permissions or roles they do not hold | `AdministrationApiTests` |
| [x] Administrator ACL bypass is audited as `ADMIN_PERMISSION_OVERRIDE` for list/grant/revoke/explain | `ShareApiTests` / admin ACL tests |

## Sharing and anonymous surfaces

| Check | Auto |
|---|---|
| [x] External links re-check the sharer's rights on every open | `ShareApiTests` |
| [x] Expired, revoked and exhausted links refuse access | `ShareApiTests` |
| [x] Wrong link password locks after `LinkPasswordMaxAttempts` | `ShareApiTests` |
| [ ] Share-link open rate limit returns 429 | staging / k6 (policy wired; confirm budget) |
| [ ] OpenAPI / Scalar are not exposed outside Development | staging (`ASPNETCORE_ENVIRONMENT=Production`) |

## Transport and browser

| Check | Auto |
|---|---|
| [ ] TLS only in production; HSTS present (`Strict-Transport-Security`) | staging (middleware sets HSTS outside Development) |
| [x] `X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY`, `Referrer-Policy: no-referrer` (+ CSP / COOP) | `SecuritySuiteTests` |
| [ ] CORS allow-list matches the real frontend origin only; credentials not `*` | staging config review |
| [x] Download responses do not embed user-controlled HTML as `text/html` | `SecuritySuiteTests` (remapped to `application/octet-stream`) |

## Data and audit

| Check | Auto |
|---|---|
| [x] Runtime DB role cannot UPDATE/DELETE/TRUNCATE `audit.audit_logs` or seals | `AuditHardeningTests` |
| [x] Seal verification detects a tampered row | `AuditHardeningTests` |
| [x] Audit export requires `AUDIT_EXPORT` and writes `AUDIT_EXPORTED` | `AuditHardeningTests` |
| [x] Object storage / filesystem paths are not guessable from document ids alone | storage keys are server-generated (`FileSystemFileStorage`) |

## Operations

| Check | Auto |
|---|---|
| [ ] Secrets (`Dms__Jwt__SigningKey`, `Dms__Audit__SealKey`, DB passwords) are not in the image or git | release review (`deploy/.env` gitignored) |
| [x] Backup contains DB + object store; restore drill completed ([backup-restore.md](backup-restore.md)) | `scripts/backup-restore-drill.sh` (DB + optional `DMS_DRILL_OBJECTS`) |
| [x] Health endpoints do not leak connection strings or versions beyond what ops needs | `SecuritySuiteTests` (`/health/*`, `/version`) |

## Staging sign-off

Remaining unchecked rows need a production-like host. Record date, environment URL, and reviewer below when closed:

- Date:
- Environment:
- Reviewer:
