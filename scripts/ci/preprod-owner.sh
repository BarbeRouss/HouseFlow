#!/usr/bin/env bash
#
# preprod-owner.sh <claim|state|require> <run-id> — qui détient la preprod (#350) ?
#
# La preprod a un NOM STABLE (`rg-houseflow-preprod`) : deux runs de `pipeline.yml` peuvent donc la
# viser en même temps (le run A attend l'approbation de la prod, le run B arrive sur `main` et reprend
# l'environnement). Le tag `owner-run` du resource group dit quel run la détient : `preprod-infra` le
# pose avant tout (un environnement neuf le reçoit de Terraform, `owner_run`), et les jobs suivants
# comme la destruction s'assurent qu'il porte encore leur identifiant. Sans lui, le job de destruction
# du run A détruirait la preprod du run B quand l'approbation de A est périmée par celle de B.
#
#   claim    pose `owner-run=<run-id>` sur le resource group existant (reprise d'un environnement vivant)
#   state    affiche `ours` (le tag vaut <run-id>), `other` (un autre run la détient) ou `absent`
#            (resource group introuvable, ou en cours de suppression) ; sort toujours en 0
#   require  sort en 1, avec un message, sauf si l'état est `ours`
#
# Reste une fenêtre de quelques secondes (A détruit pile pendant que B lit le tag) : B échoue alors
# explicitement, et il suffit de relancer son job.

set -uo pipefail

mode="${1:-}"
run="${2:-}"
[[ "$mode" =~ ^(claim|state|require)$ && "$run" =~ ^[0-9]+$ ]] || {
  echo "usage : $0 <claim|state|require> <run-id>" >&2
  exit 64
}

rg="${PREPROD_RG:-rg-houseflow-preprod}"

state() {
  local info
  info=$(az group show --name "$rg" --query '{state: properties.provisioningState, owner: tags."owner-run"}' -o json 2>/dev/null) || {
    echo absent
    return
  }
  if [ -z "$info" ] || [ "$(jq -r '.state' <<<"$info")" = "Deleting" ]; then
    echo absent
  elif [ "$(jq -r '.owner // ""' <<<"$info")" = "$run" ]; then
    echo ours
  else
    echo other
  fi
}

case "$mode" in
  claim)
    az group update --name "$rg" --set "tags.owner-run=$run" -o none || {
      echo "::error::impossible de poser le tag owner-run sur $rg"
      exit 1
    }
    echo "$rg appartient désormais au run $run."
    ;;
  state)
    state
    ;;
  require)
    current=$(state)
    if [ "$current" != "ours" ]; then
      case "$current" in
        absent) why="le resource group $rg n'existe plus (échéance dépassée, ou destruction par un autre run)" ;;
        *) why="la preprod a été reprise par un run plus récent" ;;
      esac
      echo "::error::run $run : $why — ce job ne doit pas continuer."
      {
        echo "### Preprod : arrêt"
        echo ""
        echo "$why. Rien n'a été déployé par ce job ; le run qui détient la preprod poursuit, ou il suffit de relancer ce run si elle a disparu."
      } >>"${GITHUB_STEP_SUMMARY:-/dev/null}"
      exit 1
    fi
    ;;
esac
