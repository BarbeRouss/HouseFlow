#!/bin/bash
set -euo pipefail

# HouseFlow - Session initialization script for Claude Code web sessions
# Starts Docker, restores .NET deps, installs npm deps & Playwright

PROJECT_DIR="$(cd "$(dirname "$0")/.." && pwd)"

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

echo "Session initialization complete."
