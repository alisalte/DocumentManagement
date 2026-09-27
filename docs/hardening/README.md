# Phase 9 — Hardening

Operational and security hardening for the Document Archive. Phases 1–8 delivered the product;
this phase proves it can be defended, measured and recovered.

| Deliverable | Location |
|---|---|
| Security regression tests | `tests/Dms.IntegrationTests/SecuritySuiteTests.cs` (+ auth/admin/share suites) |
| Path traversal / filename tests | `tests/Dms.Storage.UnitTests/FileNamesTests.cs` |
| Pen-test checklist | [pen-test-checklist.md](pen-test-checklist.md) |
| Performance targets | [performance-targets.md](performance-targets.md) |
| Load / abuse tests (k6) | [`load/`](../../load/) · `scripts/load-test.sh` (health, browse, search, login-abuse, share-link-abuse) |
| Dependency vulnerability scan | `scripts/security-deps.sh` |
| Container posture checks | `scripts/security-containers.sh` · non-root API + frontend images |
| TLS sample (nginx) | [`deploy/nginx/tls.conf.example`](../../deploy/nginx/tls.conf.example) |
| Backup / restore drill | [backup-restore.md](backup-restore.md) · `scripts/backup-restore-drill.sh` |

## Status

**Closed in-repo.** Environment staging sign-off (live TLS certs, production CORS origin, secrets review)
remains an ops checklist item on the target host — see [pen-test-checklist.md](pen-test-checklist.md)
“Staging sign-off”.

Done:

- Baseline security headers (CSP `frame-ancestors 'none'`, COOP, HSTS outside Development) + nginx header parity
- CORS reject `*`; empty origins = same-origin (nginx) deployments
- Dangerous download MIME types remapped to `application/octet-stream`
- Login lockout API coverage; share-link and login k6 abuse scripts in `scripts/load-test.sh`
- Path-traversal / filename sanitization unit tests; filesystem root escape guard
- ClamAV fail-closed (`ScanVerdict.Failed` throws, object stays Pending) — decision D9
- Dependency and container security scripts
- Backup/restore drill + audit integrity tests (existing)

## Acceptance mapping

| Criterion | Evidence |
|---|---|
| No unauthorized API access | `SecuritySuiteTests`, auth suites |
| No ACL bypass | Authorization unit + API tests |
| No unauthorized discovery via search | `SecuritySuiteTests` / `SearchApiTests` |
| No malicious file in permanent storage | ClamAV quarantine + fail-closed on scanner failure |
| Staging security configuration | TLS example + CORS docs; host sign-off in pen-test checklist |
