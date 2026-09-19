#!/usr/bin/env bash
# Runs every test project.
#
# The test projects are xunit v3 / Microsoft.Testing.Platform executables. On this SDK the
# `dotnet test` orchestrator reports "Zero tests ran" for them, so we run each test executable
# directly, which is a supported way to drive Microsoft.Testing.Platform.
#
# Integration tests need PostgreSQL. Start it with scripts/dev-db.sh, or point DMS_TEST_POSTGRES
# at another server. Each run creates and drops its own database.
set -uo pipefail

cd "$(dirname "$0")/.."

export DOTNET_ROOT="${DOTNET_ROOT:-$HOME/.dotnet}"
export PATH="$DOTNET_ROOT:$PATH"
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
