#!/bin/bash
set -euo pipefail

# HouseFlow - Session initialization script for Claude Code web sessions
# Installs .NET 10 SDK, GitHub CLI, starts Docker, restores deps, installs Playwright

PROJECT_DIR="$(cd "$(dirname "$0")/.." && pwd)"

# --- .NET 10 SDK ---
if dotnet --version 2>/dev/null | grep -q "^10\."; then
  echo ".NET 10 SDK already installed ($(dotnet --version))."
else
  echo "Installing .NET 10 SDK..."
  curl -sSL https://dot.net/v1/dotnet-install.sh | bash /dev/stdin --channel 10.0 --install-dir /usr/share/dotnet
  export PATH="/usr/share/dotnet:$PATH"
  echo ".NET 10 SDK installed ($(dotnet --version))."
fi

# --- GitHub CLI ---
if command -v gh &>/dev/null; then
  echo "GitHub CLI already installed ($(gh --version | head -1))."
else
  echo "Installing GitHub CLI..."
  (type -p wget >/dev/null || (apt-get update && apt-get install wget -y))
  mkdir -p -m 755 /etc/apt/keyrings
  out=$(mktemp) && wget -nv -O"$out" https://cli.github.com/packages/githubcli-archive-keyring.gpg && cat "$out" | tee /etc/apt/keyrings/githubcli-archive-keyring.gpg > /dev/null
  chmod go+r /etc/apt/keyrings/githubcli-archive-keyring.gpg
  echo "deb [arch=$(dpkg --print-architecture) signed-by=/etc/apt/keyrings/githubcli-archive-keyring.gpg] https://cli.github.com/packages stable main" | tee /etc/apt/sources.list.d/github-cli.list > /dev/null
  apt-get update && apt-get install gh -y
  echo "GitHub CLI installed ($(gh --version | head -1))."
fi

# --- Terraform ---
if command -v terraform &>/dev/null; then
  echo "Terraform already installed ($(terraform --version -json | grep -o '"terraform_version":"[^"]*"' | cut -d'"' -f4))."
else
  echo "Installing Terraform..."
  apt-get update -qq && apt-get install -y -qq gnupg software-properties-common
  wget -qO- https://apt.releases.hashicorp.com/gpg | gpg --dearmor -o /usr/share/keyrings/hashicorp-archive-keyring.gpg
  echo "deb [signed-by=/usr/share/keyrings/hashicorp-archive-keyring.gpg] https://apt.releases.hashicorp.com $(lsb_release -cs) main" | tee /etc/apt/sources.list.d/hashicorp.list
  apt-get update -qq && apt-get install -y -qq terraform
  echo "Terraform installed ($(terraform --version | head -1))."
fi

# --- Docker ---
if command -v dockerd &>/dev/null; then
  if ! docker info &>/dev/null 2>&1; then
    echo "Starting Docker daemon..."
    dockerd &>/tmp/dockerd.log &
    # Wait for Docker to be ready (max 30s)
    for i in $(seq 1 30); do
      if docker info &>/dev/null 2>&1; then
        echo "Docker daemon ready."
        break
      fi
      sleep 1
    done
    if ! docker info &>/dev/null 2>&1; then
      echo "WARNING: Docker daemon failed to start. Integration tests will not work."
    fi
  else
    echo "Docker daemon already running."
  fi
else
  echo "WARNING: Docker not installed. Integration tests will not work."
fi

# --- .NET dependencies ---
echo "Restoring .NET dependencies..."
dotnet restore "$PROJECT_DIR" --verbosity quiet

# --- Frontend dependencies ---
echo "Installing npm dependencies..."
cd "$PROJECT_DIR/src/HouseFlow.Frontend"
npm install --prefer-offline --no-audit --no-fund 2>/dev/null

# --- Playwright ---
# Try to install Playwright browsers; skip gracefully if CDN is blocked (browsers may be pre-cached)
if npx playwright install --with-deps chromium 2>/dev/null; then
  echo "Playwright browsers installed."
else
  # Check if browsers are already cached from a previous install
  if ls /root/.cache/ms-playwright/chromium-* &>/dev/null 2>&1; then
    echo "Playwright browser download failed but cached browsers found. E2E tests should work."
  else
    echo "WARNING: Playwright browsers not available. E2E tests will not work."
  fi
fi

# --- Start PostgreSQL (fallback for when Docker Hub is blocked) ---
if command -v pg_lsclusters &>/dev/null; then
  if ! pg_lsclusters 2>/dev/null | grep -q "online"; then
    echo "Starting PostgreSQL..."
    pg_ctlcluster 16 main start 2>/dev/null || true
    # Set password and create DB if needed
    su - postgres -c "psql -c \"ALTER USER postgres PASSWORD 'postgres';\"" 2>/dev/null || true
    su - postgres -c "psql -c \"CREATE DATABASE houseflow;\"" 2>/dev/null || true
    echo "PostgreSQL ready."
  else
    echo "PostgreSQL already running."
  fi
fi

echo "Session initialization complete."
echo ""
echo "To start services, run: $PROJECT_DIR/scripts/start-services.sh"
