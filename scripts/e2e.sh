#!/usr/bin/env bash
# End-to-end tests (phase 8): the real API, the real frontend, a real browser, in a phone and a
# desktop viewport. Creates a throwaway database, migrates it, starts the API as the runtime role
# on port 5091, lets Playwright start Vite on 5175, and cleans everything up afterwards.
#
# Needs PostgreSQL (scripts/dev-db.sh or the compose stack; DMS_E2E_POSTGRES overrides the
# server), the .NET SDK and Google Chrome (Playwright drives the installed one).
#
# Usage: scripts/e2e.sh [playwright arguments], e.g. scripts/e2e.sh --project=phone
set -euo pipefail

cd "$(dirname "$0")/.."

if [[ -z "${DOTNET_ROOT:-}" ]] && compgen -G "$HOME/.dotnet/sdk/10.*" >/dev/null; then
    export DOTNET_ROOT="$HOME/.dotnet"
fi
if [[ -n "${DOTNET_ROOT:-}" ]]; then
    export PATH="$DOTNET_ROOT:$PATH"
fi
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1

SERVER=${DMS_E2E_POSTGRES:-"Host=localhost;Port=5433;Username=dms;Password=change-me"}
DATABASE="dms_e2e_$(date +%s)_$RANDOM"
API_PORT=5091
WEB_PORT=5175
APP_ROLE_PASSWORD="e2e-app-role-$RANDOM$RANDOM"
export E2E_ADMIN_PASSWORD="E2eBootstrapAdmin!1"
export E2E_API_URL="http://localhost:$API_PORT"
export E2E_WEB_PORT=$WEB_PORT
STORAGE=$(mktemp -d)
API_PID=""

database() {
    dotnet run scripts/tools/e2e-database.cs -- "$1" "$SERVER" "$DATABASE" >/dev/null
}

cleanup() {
    if [[ -n "$API_PID" ]]; then kill "$API_PID" 2>/dev/null || true; wait "$API_PID" 2>/dev/null || true; fi
    database drop || true
    rm -rf "$STORAGE"
}

echo "Building..."
dotnet build DocumentManagement.slnx -v q --nologo >/dev/null

echo "Creating $DATABASE and migrating it..."
trap cleanup EXIT
database create

ConnectionStrings__Dms="$SERVER;Database=$DATABASE" \
Dms__Bootstrap__AdminPassword="$E2E_ADMIN_PASSWORD" \
Dms__Database__AppRole=dms_app \
Dms__Database__AppRolePassword="$APP_ROLE_PASSWORD" \
    dotnet src/Dms.Migrator/bin/Debug/net10.0/Dms.Migrator.dll >/dev/null

echo "Starting the API on $E2E_API_URL as dms_app..."
APP_SERVER=$(sed -e 's/Username=[^;]*/Username=dms_app/' -e "s/Password=[^;]*/Password=$APP_ROLE_PASSWORD/" <<<"$SERVER")
(
    cd src/Dms.Host
    ASPNETCORE_ENVIRONMENT=Development \
    ASPNETCORE_URLS="$E2E_API_URL" \
    ConnectionStrings__Dms="$APP_SERVER;Database=$DATABASE" \
    Dms__Jwt__SigningKey="e2e-signing-key-e2e-signing-key-e2e-signing-key-0123456789" \
    Dms__Storage__FileSystem__RootPath="$STORAGE" \
    Dms__Cors__Origins__0="http://localhost:$WEB_PORT" \
    Dms__RateLimits__LoginPerMinute=1000 \
    Logging__LogLevel__Default=Warning \
        exec dotnet bin/Debug/net10.0/Dms.Host.dll
) &
API_PID=$!

for _ in $(seq 1 60); do
    curl -sf "$E2E_API_URL/health/live" >/dev/null && break
    sleep 1
done
curl -sf "$E2E_API_URL/health/live" >/dev/null || { echo "The API did not start."; exit 1; }

cd frontend
npx playwright test "$@"
