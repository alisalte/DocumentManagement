#!/usr/bin/env bash
# Applies migrations and starts the API on http://localhost:5080.
set -euo pipefail

cd "$(dirname "$0")/.."

# A user-local SDK (see README) is used when it has .NET 10; otherwise whatever dotnet is on PATH.
if [[ -z "${DOTNET_ROOT:-}" ]] && compgen -G "$HOME/.dotnet/sdk/10.*" >/dev/null; then
    export DOTNET_ROOT="$HOME/.dotnet"
fi
if [[ -n "${DOTNET_ROOT:-}" ]]; then
    export PATH="$DOTNET_ROOT:$PATH"
fi
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_NOLOGO=1

export ConnectionStrings__Dms="${ConnectionStrings__Dms:-Host=localhost;Port=5433;Database=dms;Username=dms;Password=dms}"
export ASPNETCORE_ENVIRONMENT="${ASPNETCORE_ENVIRONMENT:-Development}"
export ASPNETCORE_URLS="${ASPNETCORE_URLS:-http://localhost:5080}"

# Without this the first sign-in is impossible, because the seeder refuses to invent a password.
if [[ -z "${Dms__Bootstrap__AdminPassword:-}" ]]; then
    echo "Set Dms__Bootstrap__AdminPassword before the first run, for example:"
    echo "  Dms__Bootstrap__AdminPassword='ChangeMe!Dev12345' $0"
    exit 1
fi

# Full-text search needs OpenSearch. Development defaults to http://localhost:9200
# (appsettings.Development.json). Start it with ./scripts/dev-search.sh when missing.
# DMS_AUTO_SEARCH=1 starts it from here; DMS_SKIP_SEARCH_CHECK=1 silences the warning.
OPENSEARCH_URL="${Dms__Search__OpenSearchUrl:-http://localhost:9200}"
if [[ "${DMS_SKIP_SEARCH_CHECK:-0}" != "1" ]]; then
    if curl -sf --max-time 2 "${OPENSEARCH_URL}" >/dev/null 2>&1; then
        echo "OpenSearch reachable at ${OPENSEARCH_URL}."
    elif [[ "${DMS_AUTO_SEARCH:-0}" == "1" ]]; then
        echo "OpenSearch not reachable; starting via scripts/dev-search.sh …"
        ./scripts/dev-search.sh
    else
        echo "Warning: OpenSearch is not reachable at ${OPENSEARCH_URL}." >&2
        echo "  Full-text search will fall back to title/description/file name until it is up." >&2
        echo "  Start it with: ./scripts/dev-search.sh" >&2
        echo "  Or auto-start: DMS_AUTO_SEARCH=1 $0" >&2
    fi
fi

echo "Applying migrations..."
dotnet run --project src/Dms.Migrator -v q --nologo

echo "Starting the API on $ASPNETCORE_URLS ..."
exec dotnet run --project src/Dms.Host -v q --nologo
