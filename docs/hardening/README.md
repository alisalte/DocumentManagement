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

**In progress.** Baseline security headers and the security suite ship with this phase opening.
Full exit criteria (green load run against agreed targets, signed pen-test checklist, successful
drill on a production-like stack) close the phase.
