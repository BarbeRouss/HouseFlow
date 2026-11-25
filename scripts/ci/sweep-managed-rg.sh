#!/usr/bin/env bash
#
# sweep-managed-rg.sh <environnement> [timeout-secondes] — vérifie, après la destruction d'une preprod
# (#350), que le resource group géré de son Container Apps Environment a disparu, et le supprime sinon.
#
#   sweep-managed-rg.sh pr-42 1800
#
# `destroy-environment.sh` rend la main sur un `az group delete --no-wait`. Azure retire le resource group
# `ME_cae-houseflow-<environnement>_<rg>_<région>` avec le CAE, mais un CAE dont la suppression échoue le
# laisse derrière lui. Le script attend donc la fin de la suppression de `rg-houseflow-<environnement>`
# (timeout, 1800 s par défaut), puis supprime tout `ME_cae-houseflow-<environnement>_*` qui subsiste. Le nom
# du groupe géré est déterministe (`environment/container-env.tf`) ; le `_` final empêche `pr-1` de
# viser `pr-12`.
#
# Codes de sortie : 0 rien ne reste, 1 le resource group n'a pas disparu dans le délai (le reaper
# reprendra, tag `ttl`), 2 la suppression d'un groupe géré a été refusée.

set -uo pipefail

name="${1:-}"
timeout="${2:-1800}"
[[ "$name" =~ ^[a-z0-9][a-z0-9-]*$ ]] || {
  echo "usage : $0 <environnement> [timeout-secondes]" >&2
  exit 64
}

rg="rg-houseflow-$name"
deadline=$((SECONDS + timeout))
while [ "$(az group exists --name "$rg")" = "true" ]; do
  if [ "$SECONDS" -ge "$deadline" ]; then
    echo "::error::$rg existe encore après ${timeout} s : le reaper reprendra la suppression."
    exit 1
  fi
  sleep 15
done
echo "$rg supprimé."

status=0
managed=$(az group list --query "[?starts_with(name, 'ME_cae-houseflow-${name}_')].name" -o tsv)
if [ -z "$managed" ]; then
  echo "Aucun resource group géré à supprimer."
  exit 0
fi
for group in $managed; do
  echo "Le resource group géré $group subsiste : suppression demandée."
  az group delete --name "$group" --yes --no-wait || {
    echo "::error::Échec de la demande de suppression de $group."
    status=2
  }
done
exit $status
