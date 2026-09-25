#!/usr/bin/env bash
# Generates a git-ignored .env with random development-only secrets for docker-compose.
set -euo pipefail
cd "$(dirname "$0")/.."
if [ -f .env ]; then echo ".env already exists; delete it to regenerate."; exit 0; fi
rand() { python3 -c "import secrets; print(secrets.token_hex(24))"; }
cat > .env <<EOT
ILM_DEV_PG_ADMIN_PASSWORD=$(rand)
ILM_DEV_PG_MIGRATOR_PASSWORD=$(rand)
ILM_DEV_PG_APP_PASSWORD=$(rand)
EOT
chmod 600 .env
echo "Wrote .env (development only, git-ignored)."
