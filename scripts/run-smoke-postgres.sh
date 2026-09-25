#!/usr/bin/env bash
# Runs the smoke test against the docker-compose PostgreSQL with production-style separation of identities:
# migrations as ilm_migrator, then the grants script, then the application as ilm_app (DML only).
# Development only: the local database has no TLS and uses the random passwords from .env.
set -euo pipefail
cd "$(dirname "$0")/.."
[ -f .env ] || ./scripts/dev-env.sh
set -a; . ./.env; set +a
docker compose up -d postgres >/dev/null
for _ in $(seq 1 60); do
  [ "$(docker inspect -f '{{.State.Health.Status}}' "$(docker compose ps -q postgres)")" = healthy ] && break
  sleep 1
done

# Fresh schema owned by the migration identity.
docker compose exec -T postgres psql -v ON_ERROR_STOP=1 -q -U postgres -d ilm -c \
  'DROP SCHEMA IF EXISTS ilm CASCADE; CREATE SCHEMA ilm AUTHORIZATION ilm_migrator; REVOKE ALL ON SCHEMA ilm FROM PUBLIC; GRANT USAGE ON SCHEMA ilm TO ilm_app, ilm_auditor_ro;'

rm -rf src/Ilm.Web/App_Data
dotnet build src/Ilm.Web -nologo -v q >/dev/null
export ASPNETCORE_ENVIRONMENT=Development
export Ilm__Database__Provider=PostgreSql
export Ilm__Development__MigrateOnStartup=false
export ConnectionStrings__IlmMigration="Host=127.0.0.1;Port=5432;Database=ilm;Username=ilm_migrator;Password=${ILM_DEV_PG_MIGRATOR_PASSWORD}"
export ConnectionStrings__Ilm="Host=127.0.0.1;Port=5432;Database=ilm;Username=ilm_app;Password=${ILM_DEV_PG_APP_PASSWORD}"

( cd src/Ilm.Web && dotnet bin/Debug/net10.0/Ilm.Web.dll migrate )
docker compose exec -T postgres psql -v ON_ERROR_STOP=1 -q -U ilm_migrator -d ilm -f /ilm-sql/20-grants-after-migration.sql

( cd src/Ilm.Web && exec dotnet bin/Debug/net10.0/Ilm.Web.dll > ../../smoke-app.log 2>&1 ) &
app_pid=$!
trap 'kill "$app_pid" 2>/dev/null || true' EXIT
for _ in $(seq 1 60); do
  if curl -sk -o /dev/null -w '%{http_code}' https://localhost:5001/health/live | grep -q 200; then break; fi
  sleep 1
done
python3 scripts/smoke_test.py https://localhost:5001
( cd src/Ilm.Web && dotnet bin/Debug/net10.0/Ilm.Web.dll verify-audit )
