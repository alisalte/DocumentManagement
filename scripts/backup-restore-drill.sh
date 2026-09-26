#!/usr/bin/env bash
# Phase 9: prove we can dump and restore PostgreSQL (and optionally object files).
# See docs/hardening/backup-restore.md.
set -euo pipefail

cd "$(dirname "$0")/.."

CONTAINER=${DMS_DRILL_CONTAINER:-dms-postgres}
HOST_PORT=${DMS_DRILL_PORT:-5433}
PGUSER=${POSTGRES_USER:-dms}
PGPASSWORD=${POSTGRES_PASSWORD:-dms}
export PGPASSWORD

DRILL_DB=${DMS_DRILL_DATABASE:-"dms_drill_$(date +%s)_$RANDOM"}
WORK=$(mktemp -d)
DUMP="$WORK/dump.sql"
MARKER="drill-marker-$(date +%s)"

cleanup() {
    if docker ps --format '{{.Names}}' | grep -qx "$CONTAINER"; then
        docker exec -e PGPASSWORD="$PGPASSWORD" "$CONTAINER" \
            psql -U "$PGUSER" -d postgres -v ON_ERROR_STOP=1 \
            -c "DROP DATABASE IF EXISTS \"$DRILL_DB\" WITH (FORCE);" >/dev/null 2>&1 || true
    fi
    rm -rf "$WORK"
}
trap cleanup EXIT

if ! docker ps --format '{{.Names}}' | grep -qx "$CONTAINER"; then
    echo "PostgreSQL container '$CONTAINER' is not running. Start it with scripts/dev-db.sh." >&2
    exit 1
fi

echo "1. Create $DRILL_DB and a marker row..."
docker exec -e PGPASSWORD="$PGPASSWORD" "$CONTAINER" \
    psql -U "$PGUSER" -d postgres -v ON_ERROR_STOP=1 \
    -c "CREATE DATABASE \"$DRILL_DB\";"
docker exec -e PGPASSWORD="$PGPASSWORD" "$CONTAINER" \
    psql -U "$PGUSER" -d "$DRILL_DB" -v ON_ERROR_STOP=1 \
    -c "CREATE TABLE drill_marker(id text PRIMARY KEY); INSERT INTO drill_marker VALUES ('$MARKER');"

echo "2. pg_dump..."
docker exec -e PGPASSWORD="$PGPASSWORD" "$CONTAINER" \
    pg_dump -U "$PGUSER" -d "$DRILL_DB" --no-owner --no-acl >"$DUMP"
test -s "$DUMP"

echo "3. Drop and recreate..."
docker exec -e PGPASSWORD="$PGPASSWORD" "$CONTAINER" \
    psql -U "$PGUSER" -d postgres -v ON_ERROR_STOP=1 \
    -c "DROP DATABASE \"$DRILL_DB\";"
docker exec -e PGPASSWORD="$PGPASSWORD" "$CONTAINER" \
    psql -U "$PGUSER" -d postgres -v ON_ERROR_STOP=1 \
    -c "CREATE DATABASE \"$DRILL_DB\";"

echo "4. Restore..."
docker exec -i -e PGPASSWORD="$PGPASSWORD" "$CONTAINER" \
    psql -U "$PGUSER" -d "$DRILL_DB" -v ON_ERROR_STOP=1 <"$DUMP" >/dev/null

echo "5. Verify marker..."
FOUND=$(docker exec -e PGPASSWORD="$PGPASSWORD" "$CONTAINER" \
    psql -U "$PGUSER" -d "$DRILL_DB" -Atc "SELECT id FROM drill_marker;")
if [[ "$FOUND" != "$MARKER" ]]; then
    echo "Marker mismatch: expected $MARKER, got $FOUND" >&2
    exit 1
fi

if [[ -n "${DMS_DRILL_OBJECTS:-}" ]]; then
    echo "6. Object-store sentinel..."
    OBJECTS=${DMS_DRILL_OBJECTS}
    mkdir -p "$OBJECTS"
    SENTINEL="$OBJECTS/.dms-drill-sentinel"
    echo "$MARKER" >"$SENTINEL"
    cp "$SENTINEL" "$WORK/sentinel.copy"
    rm -f "$SENTINEL"
    cp "$WORK/sentinel.copy" "$SENTINEL"
    [[ "$(cat "$SENTINEL")" == "$MARKER" ]]
    rm -f "$SENTINEL"
fi

echo "Backup/restore drill passed."
