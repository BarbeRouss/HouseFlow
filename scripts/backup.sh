#!/bin/bash
set -e

# PostgreSQL daily backup with 7-day retention
# Cron: 0 3 * * * /opt/houseflow/scripts/backup.sh >> /var/log/houseflow-backup.log 2>&1

BACKUP_DIR="/opt/houseflow/backups"
TIMESTAMP=$(date +%Y%m%d_%H%M%S)
RETENTION_DAYS=7
PROD_COMPOSE="/opt/houseflow/prod/docker-compose.yaml"

mkdir -p "$BACKUP_DIR"

# Dump prod DB
docker compose -f "$PROD_COMPOSE" exec -T postgres \
  pg_dump -U "$DB_USER" -Fc houseflow > "$BACKUP_DIR/houseflow_$TIMESTAMP.dump"

# Compress
gzip "$BACKUP_DIR/houseflow_$TIMESTAMP.dump"

# Cleanup old backups
find "$BACKUP_DIR" -name "*.dump.gz" -mtime +$RETENTION_DAYS -delete

echo "[$(date)] Backup completed: houseflow_$TIMESTAMP.dump.gz"
