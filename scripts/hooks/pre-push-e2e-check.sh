#!/bin/bash
# PreToolUse hook: block git push unless E2E tests were recently verified.
# Receives tool input as JSON on stdin.

INPUT=$(cat)

# Only intercept git push commands
if ! echo "$INPUT" | grep -q "git push"; then
  exit 0
fi

MARKER_FILE="/tmp/houseflow-e2e-verified"
MAX_AGE_SECONDS=60  # 1 minute — push should happen right after tests

if [ ! -f "$MARKER_FILE" ]; then
  echo "BLOCKED: E2E tests have not been verified."
  echo "Run: bash scripts/verify-e2e.sh"
  echo "Then retry the push."
  exit 1
fi

MARKER_TIME=$(cat "$MARKER_FILE")
CURRENT_TIME=$(date +%s)
AGE=$((CURRENT_TIME - MARKER_TIME))

if [ $AGE -gt $MAX_AGE_SECONDS ]; then
  echo "BLOCKED: E2E verification is stale (${AGE}s ago, max ${MAX_AGE_SECONDS}s)."
  echo "Run: bash scripts/verify-e2e.sh"
  echo "Then retry the push."
  exit 1
fi

echo "E2E verified ${AGE}s ago. Push allowed."
exit 0
