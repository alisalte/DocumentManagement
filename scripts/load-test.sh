#!/usr/bin/env bash
# Phase 9: run the k6 suite against a live API. See docs/hardening/performance-targets.md.
set -euo pipefail

cd "$(dirname "$0")/.."

if ! command -v k6 >/dev/null 2>&1; then
    echo "k6 is not installed. https://k6.io/docs/get-started/installation/" >&2
    exit 1
fi

BASE_URL=${DMS_LOAD_BASE_URL:-http://localhost:5080}
USER=${DMS_LOAD_USER:-admin}
PASSWORD=${DMS_LOAD_PASSWORD:-ChangeMe!Dev12345}
VUS=${DMS_LOAD_VUS:-20}
DURATION=${DMS_LOAD_DURATION:-2m}

echo "Target $BASE_URL (VUs=$VUS duration=$DURATION)"
curl -sf "$BASE_URL/health/live" >/dev/null || {
    echo "API is not reachable at $BASE_URL. Start it with scripts/dev-api.sh or compose." >&2
    exit 1
}

export BASE_URL USER PASSWORD VUS DURATION

k6 run -e BASE_URL="$BASE_URL" -e VUS="$VUS" -e DURATION=1m load/k6/health.js
k6 run -e BASE_URL="$BASE_URL" -e USER="$USER" -e PASSWORD="$PASSWORD" \
    -e VUS="$VUS" -e DURATION="$DURATION" load/k6/browse.js
k6 run -e BASE_URL="$BASE_URL" -e USER="$USER" -e PASSWORD="$PASSWORD" \
    -e VUS=10 -e DURATION="$DURATION" load/k6/search.js

# Abuse / rate-limit scenarios (short; expect some 429s).
k6 run -e BASE_URL="$BASE_URL" -e VUS=5 -e DURATION=20s load/k6/login-abuse.js || true
k6 run -e BASE_URL="$BASE_URL" -e VUS=8 -e DURATION=20s load/k6/share-link-abuse.js || true

echo "Load suite finished (health, browse, search, login-abuse, share-link-abuse)."
