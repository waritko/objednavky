#!/usr/bin/env bash
set -euo pipefail

cd "$(dirname "${BASH_SOURCE[0]}")/.."
step="${1:-All}"
project=backend/RestaurantOrders.SmokeTests/RestaurantOrders.SmokeTests.csproj
api=backend/RestaurantOrders.Api/RestaurantOrders.Api.csproj

case "$step" in
    All|Restore|Build|Test|Publish) ;;
    *) printf 'Unknown CI step: %s\n' "$step" >&2; exit 2 ;;
esac

if [[ "$step" == All || "$step" == Restore ]]; then
    dotnet --info
    dotnet restore "$project"
fi

if [[ "$step" == All || "$step" == Build ]]; then
    dotnet build "$project" --configuration Release --no-restore -p:ContinuousIntegrationBuild=true
fi

if [[ "$step" == All || "$step" == Test ]]; then
    if [[ -n "${TEAMCITY_VERSION:-}" ]]; then
        echo "##teamcity[testStarted name='SQLite HTTP smoke suite' captureStandardOutput='true']"
    fi
    result=0
    dotnet run --project "$project" --configuration Release --no-build --no-restore || result=$?
    if [[ -n "${TEAMCITY_VERSION:-}" ]]; then
        if [[ "$result" -ne 0 ]]; then
            echo "##teamcity[testFailed name='SQLite HTTP smoke suite' message='Smoke suite exited with code $result; see build log.']"
        fi
        echo "##teamcity[testFinished name='SQLite HTTP smoke suite']"
    fi
    if [[ "$result" -ne 0 ]]; then exit "$result"; fi
fi

if [[ "$step" == All || "$step" == Publish ]]; then
    dotnet publish "$api" --configuration Release --no-build --no-restore --output artifacts/api
fi
