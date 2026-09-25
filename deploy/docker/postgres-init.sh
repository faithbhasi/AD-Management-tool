#!/bin/sh
# Development only: creates the ILM roles and database with the same scripts used in production.
set -eu
psql -v ON_ERROR_STOP=1 -U postgres \
  -v migrator_password="$ILM_MIGRATOR_PASSWORD" \
  -v app_password="$ILM_APP_PASSWORD" \
  -f /ilm-sql/00-create-roles-and-database.sql
