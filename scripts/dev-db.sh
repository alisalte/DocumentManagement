#!/usr/bin/env bash
# Starts a PostgreSQL container for development and tests on port 5433.
#
# This is separate from deploy/docker-compose.yml on purpose: it needs no image build, so it works
# on a machine whose Docker daemon cannot reach a registry. It also never touches the unrelated
# fleetvision stack, which owns port 5432.
set -euo pipefail

CONTAINER=${CONTAINER:-dms-postgres}
PORT=${PORT:-5433}
USER=${POSTGRES_USER:-dms}
PASSWORD=${POSTGRES_PASSWORD:-dms}
DATABASE=${POSTGRES_DB:-dms}

# Prefer the official image; fall back to any locally cached Postgres if it cannot be pulled.
IMAGE=${POSTGRES_IMAGE:-postgres:18}
if ! docker image inspect "$IMAGE" >/dev/null 2>&1; then
    if ! docker pull "$IMAGE" >/dev/null 2>&1; then
        FALLBACK=$(docker images --format '{{.Repository}}:{{.Tag}}' | grep -iE 'postgres|timescale' | head -1 || true)
        if [[ -z "$FALLBACK" ]]; then
            echo "Cannot pull $IMAGE and no local PostgreSQL image is available." >&2
            echo "Pull one on a machine with registry access, or set POSTGRES_IMAGE." >&2
            exit 1
        fi
        echo "Cannot pull $IMAGE; using locally cached $FALLBACK instead."
        IMAGE=$FALLBACK
    fi
fi

if docker ps -a --format '{{.Names}}' | grep -qx "$CONTAINER"; then
    docker start "$CONTAINER" >/dev/null
    echo "Started existing container $CONTAINER."
else
    docker run -d \
        --name "$CONTAINER" \
        -e POSTGRES_USER="$USER" \
        -e POSTGRES_PASSWORD="$PASSWORD" \
        -e POSTGRES_DB="$DATABASE" \
        -p "$PORT:5432" \
        -v dms-pgdata:/var/lib/postgresql/data \
        "$IMAGE" >/dev/null
    echo "Created container $CONTAINER from $IMAGE."
fi

for _ in $(seq 1 60); do
    if docker exec "$CONTAINER" pg_isready -U "$USER" -d "$DATABASE" >/dev/null 2>&1; then
        echo "PostgreSQL is ready on localhost:$PORT (user: $USER, database: $DATABASE)."
        exit 0
    fi
    sleep 1
done

echo "PostgreSQL did not become ready in time." >&2
exit 1
