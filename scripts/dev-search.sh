#!/usr/bin/env bash
# Starts OpenSearch (and optionally Tika) for full-text search / OCR extraction.
# Point the API at them:
#   Dms__Search__OpenSearchUrl=http://localhost:9200 ./scripts/dev-api.sh
#   Dms__Search__TikaUrl=http://localhost:9998   # optional; local Tesseract works without it
#
# Prefers Docker when it works. If the daemon cannot create containers (common in nested
# overlay environments), falls back to a local OpenSearch tarball under
# ${OPENSEARCH_HOME:-$HOME/opensearch}.
set -euo pipefail

cd "$(dirname "$0")/.."

OPENSEARCH_CONTAINER=${OPENSEARCH_CONTAINER:-dms-opensearch}
OPENSEARCH_IMAGE=${OPENSEARCH_IMAGE:-opensearchproject/opensearch:2.19.3}
OPENSEARCH_VERSION=${OPENSEARCH_VERSION:-2.19.3}
OPENSEARCH_PORT=${OPENSEARCH_PORT:-9200}
OPENSEARCH_HOME=${OPENSEARCH_HOME:-$HOME/opensearch}
START_TIKA=${START_TIKA:-0}
FORCE_NATIVE=${FORCE_NATIVE:-0}

docker_cmd() {
    if docker info >/dev/null 2>&1; then
        docker "$@"
        return
    fi
    if command -v sudo >/dev/null 2>&1 && sudo docker info >/dev/null 2>&1; then
        sudo docker "$@"
        return
    fi
    return 1
}

wait_for_opensearch() {
    echo -n "Waiting for OpenSearch on :$OPENSEARCH_PORT "
    for _ in $(seq 1 90); do
        if curl -sf "http://127.0.0.1:${OPENSEARCH_PORT}" >/dev/null 2>&1; then
            echo " ready."
            return 0
        fi
        echo -n "."
        sleep 2
    done
    echo
    return 1
}

# OpenSearch needs this on many Linux hosts.
if [[ "$(uname -s)" == Linux ]]; then
    current=$(sysctl -n vm.max_map_count 2>/dev/null || echo 0)
    if [[ "${current:-0}" -lt 262144 ]]; then
        echo "Raising vm.max_map_count (was $current)…"
        sudo sysctl -w vm.max_map_count=262144 >/dev/null || true
    fi
fi

start_docker() {
    if ! command -v docker >/dev/null 2>&1 && ! command -v sudo >/dev/null 2>&1; then
        return 1
    fi
    if ! docker_cmd info >/dev/null 2>&1; then
        return 1
    fi

    if docker_cmd ps -a --format '{{.Names}}' | grep -qx "$OPENSEARCH_CONTAINER"; then
        docker_cmd start "$OPENSEARCH_CONTAINER" >/dev/null
        echo "Started existing container $OPENSEARCH_CONTAINER."
    else
        docker_cmd run -d \
            --name "$OPENSEARCH_CONTAINER" \
            -p "${OPENSEARCH_PORT}:9200" \
            -e discovery.type=single-node \
            -e DISABLE_SECURITY_PLUGIN=true \
            -e DISABLE_INSTALL_DEMO_CONFIG=true \
            -e "OPENSEARCH_JAVA_OPTS=-Xms512m -Xmx512m" \
            "$OPENSEARCH_IMAGE" >/dev/null
        echo "Created $OPENSEARCH_CONTAINER from $OPENSEARCH_IMAGE."
    fi

    if wait_for_opensearch; then
        return 0
    fi
    echo "Docker OpenSearch did not become ready; trying native install…" >&2
    docker_cmd logs "$OPENSEARCH_CONTAINER" 2>&1 | tail -20 >&2 || true
    return 1
}

start_native() {
    local root="$OPENSEARCH_HOME/opensearch-${OPENSEARCH_VERSION}"
    local tarball="$OPENSEARCH_HOME/opensearch-${OPENSEARCH_VERSION}-linux-x64.tar.gz"
    local url="https://artifacts.opensearch.org/releases/bundle/opensearch/${OPENSEARCH_VERSION}/opensearch-${OPENSEARCH_VERSION}-linux-x64.tar.gz"
    local pidfile="$OPENSEARCH_HOME/opensearch.pid"

    mkdir -p "$OPENSEARCH_HOME"

    if [[ ! -x "$root/bin/opensearch" ]]; then
        echo "Downloading OpenSearch ${OPENSEARCH_VERSION}…"
        curl -L --fail -o "$tarball" "$url"
        tar -xzf "$tarball" -C "$OPENSEARCH_HOME"
    fi

    cat > "$root/config/opensearch.yml" <<EOF
cluster.name: dms-dev
node.name: dms-node-1
path.data: data
path.logs: logs
network.host: 127.0.0.1
http.port: ${OPENSEARCH_PORT}
discovery.type: single-node
plugins.security.disabled: true
EOF

    mkdir -p "$root/data" "$root/logs"

    if curl -sf "http://127.0.0.1:${OPENSEARCH_PORT}" >/dev/null 2>&1; then
        echo "OpenSearch already answering on :$OPENSEARCH_PORT."
        return 0
    fi

    if [[ -f "$pidfile" ]] && kill -0 "$(cat "$pidfile")" 2>/dev/null; then
        echo "Native OpenSearch process is starting (pid $(cat "$pidfile"))."
    else
        echo "Starting native OpenSearch from $root …"
        (
            cd "$root"
            export OPENSEARCH_JAVA_OPTS="${OPENSEARCH_JAVA_OPTS:--Xms512m -Xmx512m}"
            export DISABLE_INSTALL_DEMO_CONFIG=true
            export DISABLE_SECURITY_PLUGIN=true
            nohup ./bin/opensearch >"$OPENSEARCH_HOME/opensearch.stdout.log" 2>&1 &
            echo $! >"$pidfile"
        )
    fi

    if wait_for_opensearch; then
        return 0
    fi
    echo "Native OpenSearch did not become ready." >&2
    tail -40 "$OPENSEARCH_HOME/opensearch.stdout.log" >&2 || true
    return 1
}

started=0
if [[ "$FORCE_NATIVE" != "1" ]]; then
    if start_docker; then
        started=1
    else
        echo "Docker path unavailable; falling back to native OpenSearch." >&2
    fi
fi

if [[ "$started" -ne 1 ]]; then
    start_native
fi

if [[ "$START_TIKA" == "1" ]]; then
    TIKA_CONTAINER=${TIKA_CONTAINER:-dms-tika}
    if docker_cmd ps -a --format '{{.Names}}' 2>/dev/null | grep -qx "$TIKA_CONTAINER"; then
        docker_cmd start "$TIKA_CONTAINER" >/dev/null
        echo "Tika on http://localhost:9998"
    elif docker_cmd info >/dev/null 2>&1; then
        docker_cmd build -t dms-tika:latest deploy/tika
        docker_cmd run -d --name "$TIKA_CONTAINER" -p 9998:9998 dms-tika:latest >/dev/null
        echo "Tika on http://localhost:9998"
    else
        echo "Tika skipped (Docker unavailable); local Tesseract still works for images/PDF." >&2
    fi
fi

cat <<EOF

OpenSearch is up: http://localhost:${OPENSEARCH_PORT}

Restart the API/worker with:
  export Dms__Search__OpenSearchUrl=http://localhost:${OPENSEARCH_PORT}
  # optional: export Dms__Search__TikaUrl=http://localhost:9998
  Dms__Bootstrap__AdminPassword='…' ./scripts/dev-api.sh

Then as admin: مدیریت → جستجو و نمایه → بازسازی نمایه
EOF
