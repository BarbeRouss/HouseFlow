#!/bin/bash
set -euo pipefail

# PostgreSQL daily backup with 7-day retention
# Cron: 0 3 * * * /opt/houseflow/scripts/backup.sh >> /var/log/houseflow-backup.log 2>&1

BACKUP_DIR="/opt/houseflow/backups"
TIMESTAMP=$(date +%Y%m%d_%H%M%S)
RETENTION_DAYS=7
PROD_COMPOSE="/opt/houseflow/prod/docker-compose.yaml"

# Load environment variables
set -a
source /opt/houseflow/prod/.env
set +a

: "${DB_USER:?DB_USER must be set in /opt/houseflow/prod/.env}"

mkdir -p "$BACKUP_DIR"
chmod 700 "$BACKUP_DIR"

# Dump prod DB
docker compose -f "$PROD_COMPOSE" exec -T postgres \
  pg_dump -U "$DB_USER" -Fc houseflow > "$BACKUP_DIR/houseflow_$TIMESTAMP.dump"

# Verify dump is non-empty
if [ ! -s "$BACKUP_DIR/houseflow_$TIMESTAMP.dump" ]; then
  echo "[$(date)] ERROR: Backup file is empty, aborting"
  rm -f "$BACKUP_DIR/houseflow_$TIMESTAMP.dump"
  exit 1
fi

# Compress and restrict permissions
gzip "$BACKUP_DIR/houseflow_$TIMESTAMP.dump"
chmod 600 "$BACKUP_DIR/houseflow_$TIMESTAMP.dump.gz"

# Cleanup old backups only if today's succeeded
find "$BACKUP_DIR" -name "*.dump.gz" -mtime +$RETENTION_DAYS -delete

echo "[$(date)] Backup completed: houseflow_$TIMESTAMP.dump.gz"
