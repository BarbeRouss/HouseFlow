#!/bin/bash
set -e

# Copy production database to preprod
# Called before each preprod deployment

PROD_COMPOSE="/opt/houseflow/prod/docker-compose.yaml"
PREPROD_COMPOSE="/opt/houseflow/preprod/docker-compose.yaml"

echo "[$(date)] Syncing prod DB to preprod..."

# 1. Dump prod
docker compose -f "$PROD_COMPOSE" exec -T postgres \
  pg_dump -U "$DB_USER" -Fc houseflow > /tmp/houseflow_prod.dump

# 2. Drop & restore in preprod
docker compose -f "$PREPROD_COMPOSE" exec -T postgres \
  dropdb -U "$DB_USER" --if-exists houseflow

docker compose -f "$PREPROD_COMPOSE" exec -T postgres \
  createdb -U "$DB_USER" houseflow

docker compose -f "$PREPROD_COMPOSE" exec -T postgres \
  pg_restore -U "$DB_USER" -d houseflow --no-owner < /tmp/houseflow_prod.dump

# 3. Cleanup
rm -f /tmp/houseflow_prod.dump

echo "[$(date)] DB sync complete."
