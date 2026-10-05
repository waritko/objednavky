#!/usr/bin/env bash
set -euo pipefail

cd "$(dirname "${BASH_SOURCE[0]}")/.."
step="${1:-All}"
project=backend/RestaurantOrders.SmokeTests/RestaurantOrders.SmokeTests.csproj
api=backend/RestaurantOrders.Api/RestaurantOrders.Api.csproj
export BUILD_CONFIGURATION=Release

case "$step" in
    All|Restore|Check|Build|Test|Publish|Browser) ;;
    *) printf 'Unknown CI step: %s\n' "$step" >&2; exit 2 ;;
esac

suite() {
    local name="$1" result=0
    shift
    if [[ -n "${TEAMCITY_VERSION:-}" ]]; then
        echo "##teamcity[testStarted name='$name' captureStandardOutput='true']"
    fi
    "$@" || result=$?
    if [[ -n "${TEAMCITY_VERSION:-}" ]]; then
        if [[ "$result" -ne 0 ]]; then
            echo "##teamcity[testFailed name='$name' message='Suite exited with code $result; see build log.']"
        fi
        echo "##teamcity[testFinished name='$name']"
    fi
    return "$result"
}

if [[ "$step" == All || "$step" == Restore ]]; then
    dotnet --info
    [[ "$(node --version)" == "v$(tr -d '\r\n' < .node-version)" ]] || { echo 'Install Node version from .node-version.' >&2; exit 1; }
    [[ "$(npm --version)" == "11.12.1" ]] || { echo 'Install npm 11.12.1.' >&2; exit 1; }
    dotnet restore "$project"
    npm --prefix frontend ci
    (cd frontend && npx playwright install chromium)
fi

if [[ "$step" == All || "$step" == Check ]]; then
    dotnet format whitespace "$api" --verify-no-changes --no-restore
    dotnet format whitespace "$project" --verify-no-changes --no-restore
    npm --prefix frontend run format:check
    npm --prefix frontend run typecheck
    git diff --check
fi

if [[ "$step" == All || "$step" == Build ]]; then
    dotnet build "$project" --configuration Release --no-restore -p:ContinuousIntegrationBuild=true
    npm --prefix frontend run build
fi

if [[ "$step" == All || "$step" == Test ]]; then
    suite 'SQLite HTTP and concurrency' dotnet run --project "$project" --configuration Release --no-build --no-restore
    suite 'SQL Server HTTP and concurrency' node scripts/sqlserver-tests.mjs
    suite 'Frontend unit tests' npm --prefix frontend test
fi

if [[ "$step" == All || "$step" == Publish ]]; then
    dotnet publish "$api" --configuration Release --no-build --no-restore --output artifacts/api
    node scripts/package.mjs
fi

if [[ "$step" == All || "$step" == Browser ]]; then
    export E2E_PUBLISHED=1
    suite 'Phone browser workflow' npm --prefix frontend run test:e2e
fi
