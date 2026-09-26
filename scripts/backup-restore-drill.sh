#!/usr/bin/env bash
# Phase 9: prove we can dump and restore PostgreSQL (and optionally object files).
# See docs/hardening/backup-restore.md.
#
# Modes:
#   docker (default) — uses container DMS_DRILL_CONTAINER (scripts/dev-db.sh)
#   local            — uses host psql/pg_dump (PGHOST/PGPORT/PGUSER/PGPASSWORD)
set -euo pipefail

cd "$(dirname "$0")/.."

MODE=${DMS_DRILL_MODE:-}
CONTAINER=${DMS_DRILL_CONTAINER:-dms-postgres}
PGHOST=${PGHOST:-localhost}
PGPORT=${PGPORT:-${DMS_DRILL_PORT:-5432}}
PGUSER=${POSTGRES_USER:-${PGUSER:-dms}}
PGPASSWORD=${POSTGRES_PASSWORD:-${PGPASSWORD:-dms}}
export PGHOST PGPORT PGUSER PGPASSWORD

DRILL_DB=${DMS_DRILL_DATABASE:-"dms_drill_$(date +%s)_$RANDOM"}
WORK=$(mktemp -d)
DUMP="$WORK/dump.sql"
MARKER="drill-marker-$(date +%s)"

if [[ -z "$MODE" ]]; then
    if command -v docker >/dev/null 2>&1 && docker ps --format '{{.Names}}' 2>/dev/null | grep -qx "$CONTAINER"; then
        MODE=docker
    elif command -v psql >/dev/null 2>&1 && pg_isready -q 2>/dev/null; then
        MODE=local
    else
        echo "No Postgres target. Start scripts/dev-db.sh (docker) or a local server, or set DMS_DRILL_MODE=local." >&2
        exit 1
    fi
fi

psql_admin() {
    if [[ "$MODE" == docker ]]; then
        docker exec -e PGPASSWORD="$PGPASSWORD" "$CONTAINER" \
            psql -U "$PGUSER" -d postgres -v ON_ERROR_STOP=1 "$@"
    else
        psql -d postgres -v ON_ERROR_STOP=1 "$@"
    fi
}

psql_db() {
    local db=$1
    shift
    if [[ "$MODE" == docker ]]; then
        docker exec -e PGPASSWORD="$PGPASSWORD" "$CONTAINER" \
            psql -U "$PGUSER" -d "$db" -v ON_ERROR_STOP=1 "$@"
    else
        psql -d "$db" -v ON_ERROR_STOP=1 "$@"
    fi
}

cleanup() {
    psql_admin -c "DROP DATABASE IF EXISTS \"$DRILL_DB\" WITH (FORCE);" >/dev/null 2>&1 || true
    rm -rf "$WORK"
}
trap cleanup EXIT

echo "Mode: $MODE (database $DRILL_DB)"

echo "1. Create $DRILL_DB and a marker row..."
psql_admin -c "CREATE DATABASE \"$DRILL_DB\";"
psql_db "$DRILL_DB" -c "CREATE TABLE drill_marker(id text PRIMARY KEY); INSERT INTO drill_marker VALUES ('$MARKER');"

echo "2. pg_dump..."
if [[ "$MODE" == docker ]]; then
    docker exec -e PGPASSWORD="$PGPASSWORD" "$CONTAINER" \
        pg_dump -U "$PGUSER" -d "$DRILL_DB" --no-owner --no-acl >"$DUMP"
else
    pg_dump -d "$DRILL_DB" --no-owner --no-acl >"$DUMP"
fi
test -s "$DUMP"

echo "3. Drop and recreate..."
psql_admin -c "DROP DATABASE \"$DRILL_DB\";"
psql_admin -c "CREATE DATABASE \"$DRILL_DB\";"

echo "4. Restore..."
if [[ "$MODE" == docker ]]; then
    docker exec -i -e PGPASSWORD="$PGPASSWORD" "$CONTAINER" \
        psql -U "$PGUSER" -d "$DRILL_DB" -v ON_ERROR_STOP=1 <"$DUMP" >/dev/null
else
    psql -d "$DRILL_DB" -v ON_ERROR_STOP=1 <"$DUMP" >/dev/null
fi

echo "5. Verify marker..."
FOUND=$(psql_db "$DRILL_DB" -Atc "SELECT id FROM drill_marker;")
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
