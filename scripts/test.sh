#!/usr/bin/env bash
# Runs every test project.
#
# The test projects are xunit v3 / Microsoft.Testing.Platform executables. On this SDK the
# `dotnet test` orchestrator reports "Zero tests ran" for them, so we run each test executable
# directly, which is a supported way to drive Microsoft.Testing.Platform.
#
# Integration tests need PostgreSQL. Start it with scripts/dev-db.sh, or point DMS_TEST_POSTGRES
# at another server. Each run creates and drops its own database.
#
# The search tests against real engines run only when these are set (otherwise they are skipped):
#   DMS_TEST_OPENSEARCH=http://localhost:9200   DMS_TEST_TIKA=http://localhost:9998
set -uo pipefail

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
export DMS_TEST_POSTGRES="${DMS_TEST_POSTGRES:-Host=localhost;Port=5433;Username=dms;Password=dms;Database=postgres}"

configuration="${1:-Debug}"

echo "Building..."
if ! dotnet build DocumentManagement.slnx -c "$configuration" -v q --nologo; then
    echo "Build failed."
    exit 1
fi

failed=0
for project in tests/*/; do
    name=$(basename "$project")
    executable="$project/bin/$configuration/net10.0/$name"
    # Windows (Git Bash) builds name.exe instead of an extensionless apphost.
    if [[ -f "$executable.exe" ]]; then
        executable="$executable.exe"
    fi
    if [[ ! -x "$executable" ]]; then
        continue
    fi

    echo
    echo "=== $name ==="
    if ! "$executable"; then
        failed=1
    fi
done

echo
if [[ $failed -eq 0 ]]; then
    echo "All test projects passed."
else
    echo "One or more test projects failed."
fi

exit $failed
