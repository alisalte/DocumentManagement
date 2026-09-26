# Performance targets (phase 9)

Business sizing (documents, storage, concurrent users, largest file) is still open in
`docs/architecture.md` §12. Until those numbers arrive, load tests use the **assumptions** below.
Replace them when the business answers; do not treat them as contractual SLOs.

## Assumptions

| Metric | Assumption | Notes |
|---|---|---|
| Concurrent interactive users | 50 | Signed-in staff browsing and filing |
| Documents in the archive | 100_000 | Mixed PDFs and Office |
| Typical file size | 2 MB | p95 under 20 MB |
| Largest file | 100 MB | Streaming upload; resumable upload deferred (R8) |
| Search QPS (sustained) | 10 | With OpenSearch profile enabled |
| Login attempts / minute / IP | ≤ default rate limit (10) | Abuse path, not UX |

## Targets for `scripts/load-test.sh`

Against a warm API + Postgres (compose or `dev-api.sh`), with OpenSearch optional:

| Scenario | k6 script | Pass if |
|---|---|---|
| Health | `load/k6/health.js` | p95 &lt; 50 ms, error rate 0 |
| Login + browse | `load/k6/browse.js` | p95 login &lt; 500 ms, list &lt; 800 ms, errors &lt; 1% |
| Search (title fallback OK) | `load/k6/search.js` | p95 &lt; 1 s, errors &lt; 1% |

Tune VUs with `DMS_LOAD_VUS` (default 20) and duration with `DMS_LOAD_DURATION` (default `2m`).
