#!/bin/bash
set -euo pipefail

# Copy production database to preprod
# Triggered on-demand via GitHub Actions workflow (sync-db.yml)

PROD_COMPOSE="/opt/houseflow/prod/docker-compose.yaml"
PREPROD_COMPOSE="/opt/houseflow/preprod/docker-compose.yaml"

# Load environment variables
set -a
source /opt/houseflow/prod/.env
set +a

: "${DB_USER:?DB_USER must be set in /opt/houseflow/prod/.env}"

# Use secure temp file with cleanup trap
DUMP_FILE=$(mktemp /tmp/houseflow_prod.XXXXXX.dump)
chmod 600 "$DUMP_FILE"
trap 'rm -f "$DUMP_FILE"' EXIT

echo "[$(date)] Syncing prod DB to preprod..."

# 1. Dump prod
docker compose -f "$PROD_COMPOSE" exec -T postgres \
  pg_dump -U "$DB_USER" -Fc houseflow > "$DUMP_FILE"

# Verify dump is non-empty
if [ ! -s "$DUMP_FILE" ]; then
  echo "[$(date)] ERROR: Prod dump is empty, aborting"
  exit 1
fi

# 2. Drop & restore in preprod
docker compose -f "$PREPROD_COMPOSE" exec -T postgres \
  dropdb -U "$DB_USER" --if-exists houseflow

docker compose -f "$PREPROD_COMPOSE" exec -T postgres \
  createdb -U "$DB_USER" houseflow

docker compose -f "$PREPROD_COMPOSE" exec -T postgres \
  pg_restore -U "$DB_USER" -d houseflow --no-owner < "$DUMP_FILE"

# Cleanup handled by trap

echo "[$(date)] DB sync complete."
