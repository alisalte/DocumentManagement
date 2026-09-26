# Phase 9 — Hardening

Operational and security hardening for the Document Archive. Phases 1–8 delivered the product;
this phase proves it can be defended, measured and recovered.

| Deliverable | Location |
|---|---|
| Security regression tests | `tests/Dms.IntegrationTests/SecuritySuiteTests.cs` (+ existing auth/admin/share suites) |
| Pen-test checklist | [pen-test-checklist.md](pen-test-checklist.md) |
| Performance targets (assumptions until business sizing arrives) | [performance-targets.md](performance-targets.md) |
| Load tests (k6) | [`load/`](../../load/) · `scripts/load-test.sh` |
| Backup / restore drill | [backup-restore.md](backup-restore.md) · `scripts/backup-restore-drill.sh` |

## Status

**Nearly closed — staging sign-off remains.**

Done in-repo / verified here:

- Baseline security headers (incl. CSP `frame-ancestors 'none'`, COOP) + `SecuritySuiteTests` green (8/8)
- Dangerous download MIME types remapped to `application/octet-stream`
- Pen-test checklist mapped to automated coverage; most rows checked via suite + drill + k6
- Backup/restore drill passed (local Postgres + object-store sentinel)
- k6 `health.js` thresholds green; `login-abuse.js` observed HTTP 429 under stuffing

Still required to mark the phase **done**:

1. Staging TLS + HSTS + CORS origin review (sign the remaining environment-only rows)
2. Full authenticated k6 browse/search suite against agreed [performance targets](performance-targets.md)
3. Share-link open 429 confirmation on staging
