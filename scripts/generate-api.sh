#!/usr/bin/env bash
set -euo pipefail

# Generate backend code from OpenAPI spec using NSwag
# Usage: ./scripts/generate-api.sh

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(dirname "$SCRIPT_DIR")"

cd "$ROOT_DIR"

echo "Generating DTOs from OpenAPI spec..."
dotnet nswag run nswag-dtos.json

echo "Generating controller bases from OpenAPI spec..."
dotnet nswag run nswag-controllers.json

echo "Code generation complete."
echo "  - DTOs:        src/HouseFlow.Application/Generated/Contracts.g.cs"
echo "  - Controllers: src/HouseFlow.API/Generated/Controllers.g.cs"
