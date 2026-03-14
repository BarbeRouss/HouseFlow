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
#   curl -sSL <raw-url> | sudo bash
#   # or
#   sudo bash setup-vm.sh
#
# What this script does:
#   1. Install Docker + Docker Compose
#   2. Create houseflow system user
#   3. Create directory structure (/opt/houseflow)
#   4. Generate docker-compose files for prod + preprod
#   5. Generate .env templates
#   6. Install deployment scripts (backup, db sync)
#   7. Configure cron for daily backups
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
  chmod a+r /etc/apt/keyrings/docker.gpg

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
      - "8080:8080"
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
    healthcheck:
      test: ["CMD", "curl", "-f", "http://localhost:8080/alive"]
      interval: 30s
      timeout: 5s
      retries: 3
      start_period: 10s

  web:
    image: ghcr.io/barberouss/houseflow-web:${IMAGE_TAG:-latest}
    container_name: houseflow-web-prod
    restart: unless-stopped
    ports:
      - "3000:3000"
    environment:
      - NEXT_PUBLIC_API_URL=${API_PUBLIC_URL:-http://localhost:8080}
    depends_on:
      api:
        condition: service_healthy

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
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U ${DB_USER} -d houseflow"]
      interval: 10s
      timeout: 5s
      retries: 5

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
      - "8180:8080"
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
    healthcheck:
      test: ["CMD", "curl", "-f", "http://localhost:8080/alive"]
      interval: 30s
      timeout: 5s
      retries: 3
      start_period: 10s

  web:
    image: ghcr.io/barberouss/houseflow-web:${IMAGE_TAG:-latest}
    container_name: houseflow-web-preprod
    restart: unless-stopped
    ports:
      - "3100:3000"
    environment:
      - NEXT_PUBLIC_API_URL=${API_PUBLIC_URL:-http://localhost:8180}
    depends_on:
      api:
        condition: service_healthy

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
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U ${DB_USER} -d houseflow"]
      interval: 10s
      timeout: 5s
      retries: 5

volumes:
  postgres_preprod_data:
COMPOSE_PREPROD

# ── 7. Generate .env templates ────────────────────
info "Generating .env files..."

# Only create .env if it doesn't exist (don't overwrite secrets)
for ENV_DIR in prod preprod; do
  ENV_FILE="$HOUSEFLOW_DIR/$ENV_DIR/.env"
  if [ -f "$ENV_FILE" ]; then
    warn ".env already exists at $ENV_FILE — skipping (won't overwrite secrets)"
  else
    if [ "$ENV_DIR" = "prod" ]; then
      cat > "$ENV_FILE" << 'ENV_PROD'
# HouseFlow Production Environment
# ⚠️  Fill in all values before starting

# Database
DB_USER=houseflow
DB_PASSWORD=CHANGE_ME_STRONG_PASSWORD

# JWT (minimum 32 characters)
JWT_KEY=CHANGE_ME_MINIMUM_32_CHARS_SECRET_KEY
JWT_ISSUER=https://api.yourdomain.com
JWT_AUDIENCE=https://app.yourdomain.com

# CORS (comma-separated origins)
CORS_ORIGINS=https://app.yourdomain.com

# Public URL for frontend to reach API
API_PUBLIC_URL=https://api.yourdomain.com

# Image tag (set by CI/CD, default: latest)
IMAGE_TAG=latest
ENV_PROD
    else
      cat > "$ENV_FILE" << 'ENV_PREPROD'
# HouseFlow Preprod Environment
# ⚠️  Fill in all values before starting

# Database
DB_USER=houseflow
DB_PASSWORD=CHANGE_ME_STRONG_PASSWORD

# JWT (minimum 32 characters)
JWT_KEY=CHANGE_ME_MINIMUM_32_CHARS_SECRET_KEY
JWT_ISSUER=https://api-preprod.yourdomain.com
JWT_AUDIENCE=https://preprod.yourdomain.com

# CORS (comma-separated origins)
CORS_ORIGINS=https://preprod.yourdomain.com

# Public URL for frontend to reach API
API_PUBLIC_URL=https://api-preprod.yourdomain.com

# Image tag (set by CI/CD, default: latest)
IMAGE_TAG=latest
ENV_PREPROD
    fi
    chmod 600 "$ENV_FILE"
  fi
done

# ── 8. Install scripts ───────────────────────────
info "Installing deployment scripts..."

# Copy scripts from repo or generate them
cat > "$HOUSEFLOW_DIR/scripts/backup.sh" << 'BACKUP_SCRIPT'
#!/bin/bash
set -e

BACKUP_DIR="/opt/houseflow/backups"
TIMESTAMP=$(date +%Y%m%d_%H%M%S)
RETENTION_DAYS=7
PROD_COMPOSE="/opt/houseflow/prod/docker-compose.yaml"

# Load prod env for DB_USER
set -a
source /opt/houseflow/prod/.env
set +a

mkdir -p "$BACKUP_DIR"

docker compose -f "$PROD_COMPOSE" exec -T postgres \
  pg_dump -U "$DB_USER" -Fc houseflow > "$BACKUP_DIR/houseflow_$TIMESTAMP.dump"

gzip "$BACKUP_DIR/houseflow_$TIMESTAMP.dump"

find "$BACKUP_DIR" -name "*.dump.gz" -mtime +$RETENTION_DAYS -delete

echo "[$(date)] Backup completed: houseflow_$TIMESTAMP.dump.gz"
BACKUP_SCRIPT

cat > "$HOUSEFLOW_DIR/scripts/sync-db-to-preprod.sh" << 'SYNC_SCRIPT'
#!/bin/bash
set -e

PROD_COMPOSE="/opt/houseflow/prod/docker-compose.yaml"
PREPROD_COMPOSE="/opt/houseflow/preprod/docker-compose.yaml"

# Load prod env for DB_USER
set -a
source /opt/houseflow/prod/.env
set +a

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
SYNC_SCRIPT

chmod +x "$HOUSEFLOW_DIR/scripts/"*.sh

# ── 9. Configure cron for backups ─────────────────
info "Setting up daily backup cron job..."
CRON_LINE="0 3 * * * $HOUSEFLOW_DIR/scripts/backup.sh >> /var/log/houseflow-backup.log 2>&1"

# Add cron job for houseflow user if not already present
(crontab -u "$HOUSEFLOW_USER" -l 2>/dev/null || true) | grep -qF "backup.sh" || \
  (crontab -u "$HOUSEFLOW_USER" -l 2>/dev/null || true; echo "$CRON_LINE") | crontab -u "$HOUSEFLOW_USER" -

# ── 10. Configure firewall ────────────────────────
info "Configuring firewall (ufw)..."
if command -v ufw &> /dev/null; then
  ufw --force enable
  ufw default deny incoming
  ufw default allow outgoing
  ufw allow ssh
  ufw allow 80/tcp    # HTTP (Traefik)
  ufw allow 443/tcp   # HTTPS (Traefik)
  # Ports 8080, 8180, 3000, 3100 are NOT exposed — Traefik proxies internally
  info "Firewall configured (SSH + HTTP/HTTPS only)"
else
  warn "ufw not found — install it manually: apt install ufw"
fi

# ── 11. Set ownership ────────────────────────────
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
echo "  │   └── .env  ← EDIT THIS"
echo "  ├── preprod/"
echo "  │   ├── docker-compose.yaml"
echo "  │   └── .env  ← EDIT THIS"
echo "  ├── scripts/"
echo "  │   ├── backup.sh"
echo "  │   └── sync-db-to-preprod.sh"
echo "  └── backups/"
echo ""
warn "Next steps:"
echo "  1. Edit secrets in $HOUSEFLOW_DIR/prod/.env"
echo "  2. Edit secrets in $HOUSEFLOW_DIR/preprod/.env"
echo "  3. Login to GHCR:"
echo "     sudo -u $HOUSEFLOW_USER docker login ghcr.io -u $GHCR_OWNER"
echo "  4. Start production:"
echo "     cd $HOUSEFLOW_DIR/prod && sudo -u $HOUSEFLOW_USER docker compose up -d"
echo "  5. Start preprod:"
echo "     cd $HOUSEFLOW_DIR/preprod && sudo -u $HOUSEFLOW_USER docker compose up -d"
echo "  6. Configure Traefik (separately) to route:"
echo "     - app.yourdomain.com     → localhost:3000"
echo "     - api.yourdomain.com     → localhost:8080"
echo "     - preprod.yourdomain.com → localhost:3100"
echo "     - api-preprod.yourdomain.com → localhost:8180"
echo "  7. Add GitHub secrets:"
echo "     - DEPLOY_HOST: VM public IP or DDNS"
echo "     - DEPLOY_USER: $HOUSEFLOW_USER"
echo "     - DEPLOY_SSH_KEY: SSH private key for $HOUSEFLOW_USER"
echo "  8. Configure GitHub Environments:"
echo "     - 'preprod': no protection rules"
echo "     - 'production': required reviewers"
echo ""
