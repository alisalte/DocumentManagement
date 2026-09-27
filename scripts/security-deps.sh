#!/usr/bin/env bash
# Phase 9: dependency vulnerability scan (NuGet + npm). Exit non-zero when known vulns exist.
set -euo pipefail

cd "$(dirname "$0")/.."

if [[ -z "${DOTNET_ROOT:-}" ]] && compgen -G "$HOME/.dotnet/sdk/10.*" >/dev/null; then
    export DOTNET_ROOT="$HOME/.dotnet"
fi
if [[ -n "${DOTNET_ROOT:-}" ]]; then
    export PATH="$DOTNET_ROOT:$PATH"
fi

failed=0

echo "=== NuGet vulnerable packages ==="
if ! dotnet restore DocumentManagement.slnx -v q --nologo; then
    echo "Restore failed." >&2
    exit 1
fi
if ! dotnet list DocumentManagement.slnx package --vulnerable --include-transitive 2>&1 | tee /tmp/dms-nuget-vuln.txt; then
    failed=1
fi
if grep -qiE 'has the following vulnerable|Severity' /tmp/dms-nuget-vuln.txt; then
    echo "Vulnerable NuGet packages reported above." >&2
    failed=1
else
    echo "No vulnerable NuGet packages reported."
fi

echo
echo "=== npm audit (frontend, production deps) ==="
if [[ -d frontend ]]; then
    (
        cd frontend
        if [[ ! -d node_modules ]]; then
            npm ci --no-audit --no-fund
        fi
        if ! npm audit --omit=dev --audit-level=high; then
            failed=1
        fi
    )
fi

exit $failed
