#!/usr/bin/env bash
#
# Test de check-self-hosted-guard.sh : il accepte les fichiers `ok-*.yml` et échoue sur chaque
# fichier `bad-*.yml` de scripts/ci/testdata/self-hosted-guard/. Lancé par `pr.yml`.

set -euo pipefail

cd "$(dirname "${BASH_SOURCE[0]}")"
guard=./check-self-hosted-guard.sh
dir=testdata/self-hosted-guard
failures=0

for file in "$dir"/ok-*.yml; do
  if "$guard" "$file" >/dev/null 2>&1; then
    echo "OK   $file accepté"
  else
    echo "FAIL $file aurait dû être accepté"
    "$guard" "$file" || true
    failures=$((failures + 1))
  fi
done

for file in "$dir"/bad-*.yml; do
  if output=$("$guard" "$file" 2>&1); then
    echo "FAIL $file aurait dû être refusé"
    failures=$((failures + 1))
  elif grep -q '^::error file=' <<<"$output"; then
    echo "OK   $file refusé"
  else
    echo "FAIL $file : échec sans message de violation (erreur d'outillage ?)"
    echo "$output"
    failures=$((failures + 1))
  fi
done

exit "$((failures > 0))"
