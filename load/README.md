# Load tests (k6)

Phase 9 load suite. Install [k6](https://k6.io/docs/get-started/installation/), point at a running
API, then:

```bash
# API on http://localhost:5080, bootstrap admin already forced to change password if needed
export DMS_LOAD_BASE_URL=http://localhost:5080
export DMS_LOAD_USER=admin
export DMS_LOAD_PASSWORD='ChangeMe!Dev12345'
./scripts/load-test.sh
```

Individual scripts:

```bash
k6 run load/k6/health.js
k6 run -e BASE_URL=$DMS_LOAD_BASE_URL load/k6/browse.js
```

Thresholds match [docs/hardening/performance-targets.md](../docs/hardening/performance-targets.md).
