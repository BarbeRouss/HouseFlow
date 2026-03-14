#!/bin/bash
set -euo pipefail

# ============================================================================
# HouseFlow — VM Setup Script
# ============================================================================
# Sets up an Ubuntu/Debian VM for hosting HouseFlow (prod + preprod)
#
# Prerequisites:
#   - Fresh Ubuntu 22.04+ or Debian 12+ VM
#   - Root or sudo access
#   - Internet connectivity
#
# Usage:
#   # Download, inspect, then run:
#   curl -sSL <raw-url> -o setup-vm.sh
#   less setup-vm.sh
#   sudo bash setup-vm.sh
#
# What this script does:
#   1. Install Docker + Docker Compose
#   2. Create houseflow system user
#   3. Create directory structure (/opt/houseflow)
#   4. Generate docker-compose files for prod + preprod
#   5. Generate .env with random secrets
#   6. Install deployment scripts (backup, db sync)
#   7. Configure systemd timer for daily backups + logrotate
#   8. Configure firewall (ufw)
# ============================================================================

HOUSEFLOW_DIR="/opt/houseflow"
HOUSEFLOW_USER="houseflow"
GHCR_OWNER="barberouss"  # GitHub Container Registry owner

# ── Colors ──────────────────────────────────────────
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

info()  { echo -e "${GREEN}[INFO]${NC} $1"; }
warn()  { echo -e "${YELLOW}[WARN]${NC} $1"; }
error() { echo -e "${RED}[ERROR]${NC} $1"; exit 1; }

# ── Check root ──────────────────────────────────────
if [ "$EUID" -ne 0 ]; then
  error "This script must be run as root (sudo bash setup-vm.sh)"
fi

echo "============================================"
echo "  HouseFlow VM Setup"
echo "============================================"
echo ""

# ── 1. System updates ──────────────────────────────
info "Updating system packages..."
apt-get update -qq
apt-get upgrade -y -qq

# ── 2. Install Docker ──────────────────────────────
if command -v docker &> /dev/null; then
  info "Docker already installed: $(docker --version)"
else
  info "Installing Docker..."
  apt-get install -y -qq ca-certificates curl gnupg

  install -m 0755 -d /etc/apt/keyrings
  curl -fsSL https://download.docker.com/linux/ubuntu/gpg | gpg --dearmor -o /etc/apt/keyrings/docker.gpg
  chmod 644 /etc/apt/keyrings/docker.gpg

  # Detect distro (ubuntu or debian)
  DISTRO=$(. /etc/os-release && echo "$ID")
  CODENAME=$(. /etc/os-release && echo "$VERSION_CODENAME")

  echo "deb [arch=$(dpkg --print-architecture) signed-by=/etc/apt/keyrings/docker.gpg] https://download.docker.com/linux/${DISTRO} ${CODENAME} stable" \
    > /etc/apt/sources.list.d/docker.list

  apt-get update -qq
  apt-get install -y -qq docker-ce docker-ce-cli containerd.io docker-buildx-plugin docker-compose-plugin

  systemctl enable docker
  systemctl start docker
  info "Docker installed: $(docker --version)"
fi

# ── 3. Create houseflow user ──────────────────────
if id "$HOUSEFLOW_USER" &>/dev/null; then
  info "User '$HOUSEFLOW_USER' already exists"
else
  info "Creating system user '$HOUSEFLOW_USER'..."
  useradd --system --create-home --shell /bin/bash "$HOUSEFLOW_USER"
  usermod -aG docker "$HOUSEFLOW_USER"
fi

# ── 4. Create directory structure ─────────────────
info "Creating directory structure..."
mkdir -p "$HOUSEFLOW_DIR"/{prod,preprod,scripts,backups}
chmod 700 "$HOUSEFLOW_DIR/backups"
chown -R "$HOUSEFLOW_USER":"$HOUSEFLOW_USER" "$HOUSEFLOW_DIR"

# ── 5. Generate docker-compose for prod ───────────
info "Generating docker-compose for prod..."
cat > "$HOUSEFLOW_DIR/prod/docker-compose.yaml" << 'COMPOSE_PROD'
services:
  api:
    image: ghcr.io/barberouss/houseflow-api:${IMAGE_TAG:-latest}
    container_name: houseflow-api-prod
    restart: unless-stopped
    ports:
      - "127.0.0.1:8080:8080"
    environment:
      - ASPNETCORE_ENVIRONMENT=Production
      - ASPNETCORE_URLS=http://+:8080
      - ConnectionStrings__houseflow=Host=postgres;Port=5432;Database=houseflow;Username=${DB_USER};Password=${DB_PASSWORD}
      - JWT__KEY=${JWT_KEY}
      - Jwt__Issuer=${JWT_ISSUER}
      - Jwt__Audience=${JWT_AUDIENCE}
      - CORS__ORIGINS=${CORS_ORIGINS}
    depends_on:
      postgres:
        condition: service_healthy
    networks:
      - internal
      - proxy
    security_opt:
      - no-new-privileges:true
    deploy:
      resources:
        limits:
          cpus: '1.0'
          memory: 512M
    healthcheck:
      test: ["CMD", "curl", "-f", "http://localhost:8080/alive"]
      interval: 30s
      timeout: 5s
      retries: 3
      start_period: 10s

  web:
    image: ghcr.io/barberouss/houseflow-frontend:${IMAGE_TAG:-latest}
    container_name: houseflow-frontend-prod
    restart: unless-stopped
    ports:
      - "127.0.0.1:3000:3000"
    environment:
      - NEXT_PUBLIC_API_URL=${API_PUBLIC_URL:-http://localhost:8080}
    depends_on:
      api:
        condition: service_healthy
    networks:
      - proxy
    security_opt:
      - no-new-privileges:true
    deploy:
      resources:
        limits:
          cpus: '0.5'
          memory: 256M
    healthcheck:
      test: ["CMD", "wget", "-qO-", "http://localhost:3000/"]
      interval: 30s
      timeout: 5s
      retries: 3
      start_period: 10s

  postgres:
    image: postgres:16-alpine
    container_name: houseflow-db-prod
    restart: unless-stopped
    ports:
      - "127.0.0.1:5432:5432"
    environment:
      - POSTGRES_USER=${DB_USER}
      - POSTGRES_PASSWORD=${DB_PASSWORD}
      - POSTGRES_DB=houseflow
    volumes:
      - postgres_prod_data:/var/lib/postgresql/data
    networks:
      - internal
    security_opt:
      - no-new-privileges:true
    deploy:
      resources:
        limits:
          cpus: '1.0'
          memory: 1G
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U ${DB_USER} -d houseflow"]
      interval: 10s
      timeout: 5s
      retries: 5

networks:
  internal:
    driver: bridge
    internal: true  # No external access — DB only reachable by API
  proxy:
    driver: bridge

volumes:
  postgres_prod_data:
COMPOSE_PROD

# ── 6. Generate docker-compose for preprod ────────
info "Generating docker-compose for preprod..."
cat > "$HOUSEFLOW_DIR/preprod/docker-compose.yaml" << 'COMPOSE_PREPROD'
services:
  api:
    image: ghcr.io/barberouss/houseflow-api:${IMAGE_TAG:-latest}
    container_name: houseflow-api-preprod
    restart: unless-stopped
    ports:
      - "127.0.0.1:8180:8080"
    environment:
      - ASPNETCORE_ENVIRONMENT=Production
      - ASPNETCORE_URLS=http://+:8080
      - ConnectionStrings__houseflow=Host=postgres;Port=5432;Database=houseflow;Username=${DB_USER};Password=${DB_PASSWORD}
      - JWT__KEY=${JWT_KEY}
      - Jwt__Issuer=${JWT_ISSUER}
      - Jwt__Audience=${JWT_AUDIENCE}
      - CORS__ORIGINS=${CORS_ORIGINS}
    depends_on:
      postgres:
        condition: service_healthy
    networks:
      - internal
      - proxy
    security_opt:
      - no-new-privileges:true
    deploy:
      resources:
        limits:
          cpus: '1.0'
          memory: 512M
    healthcheck:
      test: ["CMD", "curl", "-f", "http://localhost:8080/alive"]
      interval: 30s
      timeout: 5s
      retries: 3
      start_period: 10s

  web:
    image: ghcr.io/barberouss/houseflow-frontend:${IMAGE_TAG:-latest}
    container_name: houseflow-frontend-preprod
    restart: unless-stopped
    ports:
      - "127.0.0.1:3100:3000"
    environment:
      - NEXT_PUBLIC_API_URL=${API_PUBLIC_URL:-http://localhost:8180}
    depends_on:
      api:
        condition: service_healthy
    networks:
      - proxy
    security_opt:
      - no-new-privileges:true
    deploy:
      resources:
        limits:
          cpus: '0.5'
          memory: 256M
    healthcheck:
      test: ["CMD", "wget", "-qO-", "http://localhost:3000/"]
      interval: 30s
      timeout: 5s
      retries: 3
      start_period: 10s

  postgres:
    image: postgres:16-alpine
    container_name: houseflow-db-preprod
    restart: unless-stopped
    ports:
      - "127.0.0.1:5433:5432"
    environment:
      - POSTGRES_USER=${DB_USER}
      - POSTGRES_PASSWORD=${DB_PASSWORD}
      - POSTGRES_DB=houseflow
    volumes:
      - postgres_preprod_data:/var/lib/postgresql/data
    networks:
      - internal
    security_opt:
      - no-new-privileges:true
    deploy:
      resources:
        limits:
          cpus: '1.0'
          memory: 1G
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U ${DB_USER} -d houseflow"]
      interval: 10s
      timeout: 5s
      retries: 5

networks:
  internal:
    driver: bridge
    internal: true
  proxy:
    driver: bridge

volumes:
  postgres_preprod_data:
COMPOSE_PREPROD

# ── 7. Generate .env with random secrets ──────────
info "Generating .env files..."

generate_env() {
  local ENV_FILE="$1"
  local ENV_TYPE="$2"  # "prod" or "preprod"

  if [ -f "$ENV_FILE" ]; then
    warn ".env already exists at $ENV_FILE — skipping (won't overwrite secrets)"
    return
  fi

  # Generate random secrets
  local DB_PASS
  DB_PASS=$(openssl rand -base64 24)
  local JWT_SECRET
  JWT_SECRET=$(openssl rand -base64 48)

  if [ "$ENV_TYPE" = "prod" ]; then
    cat > "$ENV_FILE" << ENV_CONTENT
# HouseFlow Production Environment
# Generated on $(date -Iseconds)

# Database
DB_USER=houseflow
DB_PASSWORD=${DB_PASS}

# JWT (auto-generated, 48 bytes base64)
JWT_KEY=${JWT_SECRET}
JWT_ISSUER=https://api.houseflow.rouss.be
JWT_AUDIENCE=https://houseflow.rouss.be

# CORS (comma-separated origins)
CORS_ORIGINS=https://houseflow.rouss.be

# Public URL for frontend to reach API
API_PUBLIC_URL=https://api.houseflow.rouss.be

# Image tag (set by CI/CD, default: latest)
IMAGE_TAG=latest
ENV_CONTENT
  else
    cat > "$ENV_FILE" << ENV_CONTENT
# HouseFlow Preprod Environment
# Generated on $(date -Iseconds)

# Database
DB_USER=houseflow
DB_PASSWORD=${DB_PASS}

# JWT (auto-generated, 48 bytes base64)
JWT_KEY=${JWT_SECRET}
JWT_ISSUER=https://api.preprod.houseflow.rouss.be
JWT_AUDIENCE=https://preprod.houseflow.rouss.be

# CORS (comma-separated origins)
CORS_ORIGINS=https://preprod.houseflow.rouss.be

# Public URL for frontend to reach API
API_PUBLIC_URL=https://api.preprod.houseflow.rouss.be

# Image tag (set by CI/CD, default: latest)
IMAGE_TAG=latest
ENV_CONTENT
  fi

  chmod 600 "$ENV_FILE"
  info "Generated $ENV_FILE with random secrets"
}

generate_env "$HOUSEFLOW_DIR/prod/.env" "prod"
generate_env "$HOUSEFLOW_DIR/preprod/.env" "preprod"

# ── 8. Install scripts ───────────────────────────
info "Installing deployment scripts..."

cat > "$HOUSEFLOW_DIR/scripts/backup.sh" << 'BACKUP_SCRIPT'
#!/bin/bash
set -euo pipefail

BACKUP_DIR="/opt/houseflow/backups"
TIMESTAMP=$(date +%Y%m%d_%H%M%S)
RETENTION_DAYS=7
PROD_COMPOSE="/opt/houseflow/prod/docker-compose.yaml"

# Load environment
set -a
source /opt/houseflow/prod/.env
set +a

: "${DB_USER:?DB_USER must be set in /opt/houseflow/prod/.env}"

mkdir -p "$BACKUP_DIR"
chmod 700 "$BACKUP_DIR"

docker compose -f "$PROD_COMPOSE" exec -T postgres \
  pg_dump -U "$DB_USER" -Fc houseflow > "$BACKUP_DIR/houseflow_$TIMESTAMP.dump"

# Verify dump is non-empty
if [ ! -s "$BACKUP_DIR/houseflow_$TIMESTAMP.dump" ]; then
  echo "[$(date)] ERROR: Backup file is empty, aborting"
  rm -f "$BACKUP_DIR/houseflow_$TIMESTAMP.dump"
  exit 1
fi

gzip "$BACKUP_DIR/houseflow_$TIMESTAMP.dump"
chmod 600 "$BACKUP_DIR/houseflow_$TIMESTAMP.dump.gz"

# Only purge old backups if today's succeeded
find "$BACKUP_DIR" -name "*.dump.gz" -mtime +$RETENTION_DAYS -delete

echo "[$(date)] Backup completed: houseflow_$TIMESTAMP.dump.gz"
BACKUP_SCRIPT

cat > "$HOUSEFLOW_DIR/scripts/sync-db-to-preprod.sh" << 'SYNC_SCRIPT'
#!/bin/bash
set -euo pipefail

PROD_COMPOSE="/opt/houseflow/prod/docker-compose.yaml"
PREPROD_COMPOSE="/opt/houseflow/preprod/docker-compose.yaml"

# Load environment
set -a
source /opt/houseflow/prod/.env
set +a

: "${DB_USER:?DB_USER must be set in /opt/houseflow/prod/.env}"

# Secure temp file with cleanup trap
DUMP_FILE=$(mktemp /tmp/houseflow_prod.XXXXXX.dump)
chmod 600 "$DUMP_FILE"
trap 'rm -f "$DUMP_FILE"' EXIT

echo "[$(date)] Syncing prod DB to preprod..."

# 1. Dump prod
docker compose -f "$PROD_COMPOSE" exec -T postgres \
  pg_dump -U "$DB_USER" -Fc houseflow > "$DUMP_FILE"

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

echo "[$(date)] DB sync complete."
SYNC_SCRIPT

chmod +x "$HOUSEFLOW_DIR/scripts/"*.sh

# ── 9. Configure systemd timer for backups ────────
info "Setting up daily backup systemd timer..."

cat > /etc/systemd/system/houseflow-backup.service << SYSTEMD_SERVICE
[Unit]
Description=HouseFlow daily PostgreSQL backup
After=docker.service
Requires=docker.service

[Service]
Type=oneshot
User=$HOUSEFLOW_USER
ExecStart=$HOUSEFLOW_DIR/scripts/backup.sh
StandardOutput=append:/var/log/houseflow-backup.log
StandardError=append:/var/log/houseflow-backup.log
SYSTEMD_SERVICE

cat > /etc/systemd/system/houseflow-backup.timer << 'SYSTEMD_TIMER'
[Unit]
Description=Run HouseFlow backup daily at 03:00

[Timer]
OnCalendar=*-*-* 03:00:00
Persistent=true

[Install]
WantedBy=timers.target
SYSTEMD_TIMER

systemctl daemon-reload
systemctl enable --now houseflow-backup.timer
info "Systemd timer enabled (daily at 03:00)"

# ── 10. Configure logrotate ──────────────────────
info "Configuring logrotate for backup logs..."
touch /var/log/houseflow-backup.log
chmod 640 /var/log/houseflow-backup.log
chown "$HOUSEFLOW_USER":adm /var/log/houseflow-backup.log

cat > /etc/logrotate.d/houseflow << 'LOGROTATE'
/var/log/houseflow-backup.log {
    weekly
    rotate 4
    compress
    missingok
    notifempty
}
LOGROTATE

# ── 11. Configure firewall ───────────────────────
info "Configuring firewall (ufw)..."
if command -v ufw &> /dev/null; then
  ufw --force enable
  ufw default deny incoming
  ufw default allow outgoing
  ufw allow ssh
  ufw allow 80/tcp    # HTTP (Traefik)
  ufw allow 443/tcp   # HTTPS (Traefik)
  # App ports bound to 127.0.0.1 only — not exposed to network
  info "Firewall configured (SSH + HTTP/HTTPS only)"
else
  warn "ufw not found — install it manually: apt install ufw"
fi

# ── 12. Set ownership ────────────────────────────
chown -R "$HOUSEFLOW_USER":"$HOUSEFLOW_USER" "$HOUSEFLOW_DIR"

# ── Done ──────────────────────────────────────────
echo ""
echo "============================================"
echo "  Setup Complete!"
echo "============================================"
echo ""
info "Directory structure:"
echo "  $HOUSEFLOW_DIR/"
echo "  ├── prod/"
echo "  │   ├── docker-compose.yaml"
echo "  │   └── .env  (random secrets generated)"
echo "  ├── preprod/"
echo "  │   ├── docker-compose.yaml"
echo "  │   └── .env  (random secrets generated)"
echo "  ├── scripts/"
echo "  │   ├── backup.sh"
echo "  │   └── sync-db-to-preprod.sh"
echo "  └── backups/"
echo ""
warn "Next steps:"
echo "  1. Review and adjust domain URLs in .env files:"
echo "     sudo -u $HOUSEFLOW_USER cat $HOUSEFLOW_DIR/prod/.env"
echo "     sudo -u $HOUSEFLOW_USER cat $HOUSEFLOW_DIR/preprod/.env"
echo "  2. Login to GHCR:"
echo "     sudo -u $HOUSEFLOW_USER docker login ghcr.io -u $GHCR_OWNER"
echo "  3. Start production:"
echo "     cd $HOUSEFLOW_DIR/prod && sudo -u $HOUSEFLOW_USER docker compose up -d"
echo "  4. Start preprod:"
echo "     cd $HOUSEFLOW_DIR/preprod && sudo -u $HOUSEFLOW_USER docker compose up -d"
echo "  5. Configure Traefik (separately) to route:"
echo "     - houseflow.rouss.be     → localhost:3000"
echo "     - api.houseflow.rouss.be     → localhost:8080"
echo "     - preprod.houseflow.rouss.be → localhost:3100"
echo "     - api.preprod.houseflow.rouss.be → localhost:8180"
echo "  6. Add GitHub secrets:"
echo "     - DEPLOY_HOST: VM public IP or DDNS"
echo "     - DEPLOY_USER: $HOUSEFLOW_USER"
echo "     - DEPLOY_SSH_KEY: SSH private key for $HOUSEFLOW_USER"
echo "  7. Configure GitHub Environments:"
echo "     - 'preprod': no protection rules"
echo "     - 'production': required reviewers"
echo ""
