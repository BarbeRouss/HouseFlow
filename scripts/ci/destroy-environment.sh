#!/usr/bin/env bash
# Détruit un environnement jetable : ses enregistrements DNS chez OVH, puis son
# resource group Azure.
#
#   destroy-environment.sh <resource-group> [--dry-run]
#
# Un seul argument, parce qu'un environnement se décrit lui-même : ses tags
# portent tout ce qu'il faut pour le détruire. Le script lit donc la vérité là
# où elle est — dans Azure — et non dans un state Terraform, qui peut être
# perdu, verrouillé ou laissé à moitié écrit par un apply interrompu. C'est
# exactement ce qui rend ce script utilisable par le reaper (reaper.yml) comme
# par la fermeture d'une PR (pr-preview.yml) : les deux chemins de destruction
# appellent le même code et couvrent donc le même périmètre.
#
# Avant, ils divergeaient : le cleanup faisait un `terraform destroy` (complet
# mais enlisable — constaté à 30 min sur la suppression des administrateurs
# Entra de PostgreSQL) et le reaper un `az group delete` (increvable mais
# aveugle au DNS, qui ne vit pas dans un resource group). Voir #227.
#
# L'ordre n'est pas négociable : le DNS d'abord. `az group delete --no-wait`
# rend la main immédiatement, et le tag qui dit quoi supprimer disparaît avec
# le groupe.
#
# Codes de sortie, parce que les deux échecs n'ont pas la même gravité et que
# l'appelant doit pouvoir les distinguer sans interroger Azure — juste après un
# `--no-wait`, le groupe existe encore, en état « Deleting » :
#   0  tout est parti
#   1  le resource group est en cours de suppression, mais le DNS a résisté
#   2  la suppression du resource group a été refusée (c'est là qu'est l'argent)
set -uo pipefail

RG=${1:?"usage: destroy-environment.sh <resource-group> [--dry-run]"}
DRY_RUN=false
[ "${2:-}" = "--dry-run" ] && DRY_RUN=true

OVH_ENDPOINT_HOST=${OVH_ENDPOINT_HOST:-eu.api.ovh.com}
DOMAIN=${DOMAIN:-houseflow.cloud}

fail=0

# ── Ce que l'environnement dit de lui-même ───────────
tags=$(az group show --name "$RG" --query tags -o json 2>/dev/null)
if [ -z "$tags" ] || [ "$tags" = "null" ]; then
  echo "::warning::$RG est introuvable — rien à détruire."
  exit 0
fi

# Le tag est écrit par le Terraform qui a réellement créé les enregistrements
# (environment/main.tf). Son absence signale un environnement antérieur à #227 :
# on le signale, et on détruit quand même le resource group — laisser du compute
# facturé pour un enregistrement DNS orphelin serait le mauvais arbitrage.
hosts=$(echo "$tags" | jq -r '.["dns-hosts"] // empty')
if [ -z "$hosts" ]; then
  echo "::warning::$RG n'a pas de tag dns-hosts : ses enregistrements DNS devront être retirés à la main."
fi

# ── 1. Les enregistrements OVH ───────────────────────
#
# Tolérant par principe : une erreur de l'API OVH ne doit pas empêcher la
# suppression du resource group, où est l'argent. On journalise, on continue, et
# le code de sortie non nul laisse le job rouge pour qu'on le sache.
ovh_api() {
  local method=$1 path=$2 body=${3:-}
  local ts sig
  ts=$(date +%s)
  sig="\$1\$$(printf '%s+%s+%s+https://%s/1.0%s+%s+%s' \
    "$OVH_APPLICATION_SECRET" "$OVH_CONSUMER_KEY" "$method" \
    "$OVH_ENDPOINT_HOST" "$path" "$body" "$ts" | sha1sum | cut -d' ' -f1)"
  curl -sS -X "$method" "https://$OVH_ENDPOINT_HOST/1.0$path" \
    -H "X-Ovh-Application: $OVH_APPLICATION_KEY" \
    -H "X-Ovh-Consumer: $OVH_CONSUMER_KEY" \
    -H "X-Ovh-Timestamp: $ts" \
    -H "X-Ovh-Signature: $sig" \
    -H "Content-Type: application/json" \
    ${body:+-d "$body"}
}

if [ -n "$hosts" ]; then
  deleted=0
  IFS=',' read -ra subdomains <<< "$hosts"
  for sub in "${subdomains[@]}"; do
    [ -z "$sub" ] && continue
    # Recherche par sous-domaine seul, sans filtrer le type : un hôte porte un
    # CNAME, et `asuid.*` un TXT. Interroger par sous-domaine les traite
    # uniformément et survit à un changement de type.
    ids=$(ovh_api GET "/domain/zone/$DOMAIN/record?subDomain=$sub" | jq -r '.[]? // empty')
    if [ -z "$ids" ]; then
      echo "  $sub : aucun enregistrement."
      continue
    fi
    for id in $ids; do
      if [ "$DRY_RUN" = "true" ]; then
        echo "  $sub : simulation, l'enregistrement $id serait supprimé."
        continue
      fi
      if ovh_api DELETE "/domain/zone/$DOMAIN/record/$id" >/dev/null; then
        echo "  $sub : enregistrement $id supprimé."
        deleted=$((deleted + 1))
      else
        echo "::error::Échec de la suppression de l'enregistrement $id ($sub)."
        fail=1
      fi
    done
  done

  # Une modification d'enregistrement n'est visible qu'après rafraîchissement de
  # la zone : sans cet appel, OVH garde les suppressions en attente.
  if [ "$DRY_RUN" != "true" ] && [ "$deleted" -gt 0 ]; then
    if ovh_api POST "/domain/zone/$DOMAIN/refresh" >/dev/null; then
      echo "Zone $DOMAIN rafraîchie ($deleted enregistrement(s) supprimé(s))."
    else
      echo "::error::Zone $DOMAIN non rafraîchie — les suppressions restent en attente."
      fail=1
    fi
  fi
fi

# ── 2. Le resource group ─────────────────────────────
if [ "$DRY_RUN" = "true" ]; then
  echo "Simulation : $RG serait supprimé."
  exit $fail
fi

# `--no-wait` : Azure supprime l'arborescence côté serveur, à son rythme et sans
# l'ordonnancement enfant par enfant de Terraform, qui est précisément ce qui
# s'enlisait. Un échec tardif n'est pas perdu — le groupe garde son tag `ttl`
# expiré et le passage suivant du reaper le reprendra.
if az group delete --name "$RG" --yes --no-wait; then
  echo "Suppression de $RG demandée."
else
  echo "::error::Échec de la demande de suppression de $RG."
  exit 2
fi

exit $fail
