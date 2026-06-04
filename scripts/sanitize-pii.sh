#!/usr/bin/env bash
# sanitize-pii.sh — Anonymise PII after syncing prod → preprod
# Usage: ./scripts/sanitize-pii.sh [CONNECTION_STRING]
#
# Default connection string targets a local preprod database.
# NEVER run this against a production database.

set -euo pipefail

CONN="${1:-Host=localhost;Port=5432;Database=houseflow_preprod;Username=postgres;Password=postgres}"

# Parse connection string into psql-compatible variables
DB_HOST=$(echo "$CONN" | grep -oP 'Host=\K[^;]+')
DB_PORT=$(echo "$CONN" | grep -oP 'Port=\K[^;]+')
DB_NAME=$(echo "$CONN" | grep -oP 'Database=\K[^;]+')
DB_USER=$(echo "$CONN" | grep -oP 'Username=\K[^;]+')
DB_PASS=$(echo "$CONN" | grep -oP 'Password=\K[^;]+')

export PGPASSWORD="$DB_PASS"
PSQL="psql -h $DB_HOST -p $DB_PORT -U $DB_USER -d $DB_NAME -v ON_ERROR_STOP=1"

echo "=== PII Sanitization ==="
echo "Target: $DB_NAME @ $DB_HOST:$DB_PORT"
echo ""

# Safety check: refuse to run against a database named exactly "houseflow" (prod)
if [[ "$DB_NAME" == "houseflow" ]]; then
    echo "ERROR: Refusing to sanitize the production database ('houseflow')."
    echo "       This script must target a preprod/staging copy."
    exit 1
fi

echo "[1/5] Sanitizing Users (emails, names, passwords)..."
$PSQL -q <<'SQL'
UPDATE "Users" SET
    "Email"        = 'user' || "Id"::text || '@fake.local',
    "FirstName"    = 'Prénom_' || LEFT("Id"::text, 8),
    "LastName"     = 'Nom_' || LEFT("Id"::text, 8),
    "PasswordHash" = '$2a$11$AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA'
WHERE "IsDeleted" = false OR "IsDeleted" = true;
SQL

echo "[2/5] Sanitizing RefreshTokens (tokens, IPs)..."
$PSQL -q <<'SQL'
UPDATE "RefreshTokens" SET
    "Token"           = 'sanitized_' || "Id"::text,
    "CreatedByIp"     = '0.0.0.0',
    "RevokedByIp"     = CASE WHEN "RevokedByIp" IS NOT NULL THEN '0.0.0.0' ELSE NULL END,
    "ReplacedByToken" = CASE WHEN "ReplacedByToken" IS NOT NULL THEN 'sanitized_replaced' ELSE NULL END;
SQL

echo "[3/5] Sanitizing AuditLogs (usernames, IPs, user agents, values)..."
$PSQL -q <<'SQL'
UPDATE "AuditLogs" SET
    "Username"          = CASE WHEN "Username" IS NOT NULL THEN 'sanitized@fake.local' ELSE NULL END,
    "IpAddress"         = CASE WHEN "IpAddress" IS NOT NULL THEN '0.0.0.0' ELSE NULL END,
    "UserAgent"         = CASE WHEN "UserAgent" IS NOT NULL THEN 'sanitized' ELSE NULL END,
    "OldValues"         = CASE WHEN "OldValues" IS NOT NULL THEN '{}' ELSE NULL END,
    "NewValues"         = CASE WHEN "NewValues" IS NOT NULL THEN '{}' ELSE NULL END,
    "ChangedProperties" = CASE WHEN "ChangedProperties" IS NOT NULL THEN '[]' ELSE NULL END;
SQL

echo "[4/5] Sanitizing Invitations (tokens)..."
$PSQL -q <<'SQL'
UPDATE "Invitations" SET
    "Token" = 'inv_sanitized_' || "Id"::text;
SQL

echo "[5/5] Verification..."
REMAINING=$($PSQL -t -c "
    SELECT count(*) FROM \"Users\"
    WHERE \"Email\" NOT LIKE '%@fake.local';
")
REMAINING=$(echo "$REMAINING" | tr -d ' ')

if [[ "$REMAINING" -eq 0 ]]; then
    echo ""
    echo "Sanitization complete. All PII has been anonymized."
else
    echo ""
    echo "WARNING: $REMAINING user(s) still have non-sanitized emails."
    exit 1
fi
