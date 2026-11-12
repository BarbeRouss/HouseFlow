#!/usr/bin/env bash
#
# purge-preview-images.sh — supprime des versions des paquets GHCR des previews (issue #352).
#
#   purge-preview-images.sh pr <n>     les versions taguées `pr-<n>` des trois paquets
#   purge-preview-images.sh orphans    les versions taguées `pr-<n>` d'une PR FERMÉE, et les versions
#                                      sans tag de plus d'un jour (anciens contenus d'un tag `pr-<n>`
#                                      repoussé à chaque push)
#
# Tourne sur un runner HÉBERGÉ avec `packages: write` (GITHUB_TOKEN) ; le dépôt doit avoir le rôle
# Admin sur chacun des paquets (deploy/preview/README.md). Variables : GH_TOKEN, GITHUB_REPOSITORY.
# `provenance`/`sbom` sont désactivés au build : une version sans tag n'est jamais l'attestation
# d'une image vivante.

set -euo pipefail

readonly PACKAGES=(houseflow-preview-api houseflow-preview-web houseflow-preview-dbtools)
readonly UNTAGGED_MIN_AGE_SECONDS=86400

: "${GH_TOKEN:?GH_TOKEN requis}"
: "${GITHUB_REPOSITORY:?GITHUB_REPOSITORY requis}"
readonly OWNER="${GITHUB_REPOSITORY%%/*}"
readonly API_OWNER="users/${OWNER,,}" # compte utilisateur (pas une organisation)

mode="${1:-}"
pr="${2:-}"
case "$mode" in
pr) [[ "$pr" =~ ^[1-9][0-9]*$ ]] || {
  echo "usage : $0 pr <n>" >&2
  exit 2
} ;;
orphans) ;;
*)
  echo "usage : $0 pr <n> | orphans" >&2
  exit 2
  ;;
esac

declare -A pr_state=()
state_of() {
  local n="$1"
  if [ -z "${pr_state[$n]:-}" ]; then
    pr_state[$n]=$(gh api "repos/$GITHUB_REPOSITORY/pulls/$n" --jq .state 2>/dev/null || echo unknown)
  fi
  printf '%s' "${pr_state[$n]}"
}

deleted=0
failed=0
for package in "${PACKAGES[@]}"; do
  # Un paquet jamais publié répond 404 : rien à supprimer.
  if ! versions=$(gh api --paginate "$API_OWNER/packages/container/$package/versions?per_page=100" \
    --jq '.[] | "\(.id)|\((.metadata.container.tags // []) | join(","))|\(now - (.updated_at | fromdateiso8601) | floor)"' 2>&1); then
    if grep -q 'Not Found\|404' <<<"$versions"; then
      echo "$package : paquet absent, rien à supprimer."
      continue
    fi
    echo "::error::$package : lecture des versions impossible : $versions"
    failed=1
    continue
  fi

  # `|` et non une tabulation : `read` fusionnerait les tabulations consécutives d'un champ vide.
  while IFS='|' read -r id tags age; do
    [ -n "$id" ] || continue
    reason=""
    if [ "$mode" = "pr" ]; then
      case ",$tags," in *",pr-$pr,"*) reason="tag pr-$pr" ;; esac
    elif [ -z "$tags" ]; then
      [ "$age" -ge "$UNTAGGED_MIN_AGE_SECONDS" ] && reason="version sans tag de plus d'un jour"
    else
      for tag in ${tags//,/ }; do
        if [[ "$tag" =~ ^pr-([0-9]+)$ ]] && [ "$(state_of "${BASH_REMATCH[1]}")" = "closed" ]; then
          reason="tag $tag, PR fermée"
        fi
      done
    fi
    [ -n "$reason" ] || continue

    if gh api -X DELETE "$API_OWNER/packages/container/$package/versions/$id" >/dev/null 2>&1; then
      echo "$package : version $id supprimée ($reason)."
      deleted=$((deleted + 1))
    else
      echo "::error::$package : suppression de la version $id impossible ($reason) — le dépôt a-t-il le rôle Admin sur le paquet ?"
      failed=1
    fi
  done <<<"$versions"
done

echo "Versions supprimées : $deleted."
exit "$failed"
