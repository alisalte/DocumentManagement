#!/usr/bin/env bash
# Phase 9: container posture checks for the API and frontend images.
# Uses trivy when available; always verifies Dockerfile USER / non-root markers.
set -euo pipefail

cd "$(dirname "$0")/.."

failed=0

echo "=== Dockerfile non-root checks ==="
if ! grep -qE '^USER[[:space:]]+dms' deploy/Dockerfile; then
    echo "deploy/Dockerfile must run as USER dms" >&2
    failed=1
else
    echo "API image: USER dms present."
fi

if ! grep -qE '^USER[[:space:]]+' deploy/frontend.Dockerfile; then
    echo "deploy/frontend.Dockerfile should drop root (USER nginx or equivalent)." >&2
    failed=1
else
    echo "Frontend image: USER directive present."
fi

if command -v trivy >/dev/null 2>&1; then
    echo
    echo "=== trivy filesystem scan (deploy/) ==="
    trivy fs --severity HIGH,CRITICAL --exit-code 1 deploy/ || failed=1
else
    echo
    echo "trivy not installed; Dockerfile checks only. Install: https://aquasecurity.github.io/trivy/"
fi

exit $failed
