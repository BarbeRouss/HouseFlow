#!/bin/bash
# Attend la fin du workflow "Infrastructure" déclenché par le même commit, s'il existe.
#
# Un push sur main qui touche à la fois l'infra (DNS, Key Vault…) et une app
# déclenche les deux workflows en parallèle. Or Azure refuse un hostname custom
# tant que son TXT asuid.* n'est pas résolvable : l'apply DNS doit précéder le
# déploiement. Ce script sérialise sans coupler les workflows.
set -euo pipefail

for i in $(seq 1 60); do
  RUN=$(gh run list --repo "$GITHUB_REPOSITORY" --workflow Infrastructure --commit "$GITHUB_SHA" \
        --json status,conclusion,url --jq '.[0] // empty')
  if [ -z "$RUN" ]; then
    echo "Pas de run Infrastructure pour ce commit — rien à attendre."
    exit 0
  fi
  STATUS=$(jq -r .status <<<"$RUN")
  CONCLUSION=$(jq -r .conclusion <<<"$RUN")
  URL=$(jq -r .url <<<"$RUN")
  if [ "$STATUS" = "completed" ]; then
    if [ "$CONCLUSION" = "success" ]; then
      echo "Infrastructure terminé avec succès : $URL"
      exit 0
    fi
    echo "::error::Infrastructure a échoué ($CONCLUSION) : $URL — déploiement annulé."
    exit 1
  fi
  echo "Infrastructure en cours ($STATUS) — attente 20 s ($i/60)"
  sleep 20
done

echo "::error::Infrastructure toujours en cours après 20 min — déploiement annulé."
exit 1
