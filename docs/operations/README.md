# Phase 10 — Production cutover & deferred seams

Phases 1–8 shipped the product; phase 9 hardens it. Phase 10 is the cutover track:
operational go-live, plus the deferred platform seams named in architecture decisions
**D11** (legacy import) and **D12** (retention / legal hold).

| Deliverable | Location |
|---|---|
| Go-live runbook | [go-live.md](go-live.md) |
| Release checklist | [release-checklist.md](release-checklist.md) |
| Retention & legal hold (D12 / 10.2–10.3) | [retention.md](retention.md) · policies, worker, `legal_holds` |
| Disposition & Certificate of Destruction (10.4) | [disposition.md](disposition.md) · review / destroy / COD |
| Record management (10.1) | [records.md](records.md) · `documents.records` / classes / series |
| Legacy import skeleton (D11) | [../legacy-import/](../legacy-import/) · `scripts/legacy-import.sh` |
| Build / role identity | `GET /version` on the API host |
| Email channel seam | `INotificationChannel` (null/no-op until SMTP is configured) |

## Status

**In progress.** Record management (10.1), retention policies + worker (10.2), Legal Hold (10.3),
and disposition / Certificate of Destruction (10.4) are implemented. Classification, SMTP, full
import, and go-live drill work remain.
