#!/bin/bash
# Sérialise un déploiement derrière les workflows d'infra déclenchés par le même commit.
#
# Un push sur main qui touche à la fois l'infra et une app lance les workflows en
# parallèle. Or un domaine custom exige, au moment de l'apply applicatif, que le
# DNS (TXT asuid.*) soit posé ET que le certificat wildcard soit disponible sur
# l'environnement. On attend donc, s'ils existent pour ce commit :
#   1. Infrastructure (apply main + DNS) ;
#   2. Certificate, qu'Infrastructure déclenche à sa fin (workflow_run).
set -euo pipefail

# Affiche le run le plus récent d'un workflow pour ce commit (vide si aucun).
latest_run() {
  gh run list --repo "$GITHUB_REPOSITORY" --workflow "$1" --commit "$GITHUB_SHA" \
    --json status,conclusion,url --jq '.[0] // empty' 2>/dev/null || true
}

# wait_for <workflow> <polls-max> : 0 = OK ou absent, 1 = échec/timeout.
wait_for() {
  local workflow=$1 max=$2 empty=0 run status conclusion url
  for i in $(seq 1 "$max"); do
    run=$(latest_run "$workflow")
    if [ -z "$run" ]; then
      # Un run peut être créé quelques secondes après le nôtre : on n'accepte
      # "aucun run" qu'après trois lectures vides consécutives.
      empty=$((empty + 1))
      if [ "$empty" -ge 3 ]; then
        echo "Pas de run $workflow pour ce commit — rien à attendre."
        return 0
      fi
      sleep 10
      continue
    fi
    empty=0
    status=$(jq -r .status <<<"$run")
    conclusion=$(jq -r .conclusion <<<"$run")
    url=$(jq -r .url <<<"$run")
    if [ "$status" = "completed" ]; then
      if [ "$conclusion" = "success" ]; then
        echo "$workflow terminé avec succès : $url"
        return 0
      fi
      echo "::error::$workflow a échoué ($conclusion) : $url — déploiement annulé."
      return 1
    fi
    echo "$workflow en cours ($status) — attente 20 s ($i/$max)"
    sleep 20
  done
  echo "::error::$workflow toujours en cours après l'attente maximale — déploiement annulé."
  return 1
}

# Les runs peuvent rester plus d'une heure en file d'attente GitHub avant de
# démarrer (constaté sur ce dépôt) : l'attente doit couvrir cette latence.
wait_for Infrastructure 360 || exit 1

# Certificate n'est déclenché qu'à la fin d'Infrastructure : s'il y a eu un run
# Infrastructure pour ce commit, on attend aussi le sien.
if [ -n "$(latest_run Infrastructure)" ]; then
  wait_for Certificate 360 || exit 1
fi
