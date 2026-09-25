#!/usr/bin/env bash
# Starts a fresh Development instance (fictional data), runs the smoke test, then stops the instance.
set -euo pipefail
cd "$(dirname "$0")/.."
rm -rf src/Ilm.Web/App_Data
dotnet build src/Ilm.Web -nologo -v q >/dev/null
export ASPNETCORE_ENVIRONMENT=Development
( cd src/Ilm.Web && exec dotnet bin/Debug/net10.0/Ilm.Web.dll > ../../smoke-app.log 2>&1 ) &
app_pid=$!
trap 'kill "$app_pid" 2>/dev/null || true' EXIT
for _ in $(seq 1 60); do
  if curl -sk -o /dev/null -w '%{http_code}' https://localhost:5001/health/live | grep -q 200; then break; fi
  sleep 1
done
python3 scripts/smoke_test.py https://localhost:5001
