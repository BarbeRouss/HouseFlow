#!/usr/bin/env bash
#
# preprod-comment.sh <pr> — crée ou met à jour LE commentaire de preprod d'une PR (#350).
#
# Le corps arrive sur l'entrée standard, sans marqueur : le script l'ajoute en tête, retrouve le
# commentaire existant par ce marqueur et le remplace, sinon en crée un. Jamais de doublon, et
# distinct du commentaire de la preview homelab (`<!-- houseflow-preview-homelab -->`).
#
# Variables : GH_TOKEN, GITHUB_REPOSITORY.

set -euo pipefail

pr="${1:-}"
[[ "$pr" =~ ^[1-9][0-9]*$ ]] || {
  echo "usage : $0 <pr>  (corps du commentaire sur l'entrée standard)" >&2
  exit 2
}
: "${GH_TOKEN:?GH_TOKEN requis}"
: "${GITHUB_REPOSITORY:?GITHUB_REPOSITORY requis}"

readonly MARKER='<!-- houseflow-preprod -->'

body="$MARKER
$(cat)"

ids=$(gh api --paginate "repos/$GITHUB_REPOSITORY/issues/$pr/comments" \
  --jq '.[] | select(.user.login == "github-actions[bot]") | select(.body | contains("<!-- houseflow-preprod -->")) | .id')
existing_id="${ids%%$'\n'*}" # le premier, s'il y en a

if [ -n "$existing_id" ]; then
  gh api -X PATCH "repos/$GITHUB_REPOSITORY/issues/comments/$existing_id" -f body="$body" >/dev/null
  echo "Commentaire $existing_id mis à jour."
else
  gh api -X POST "repos/$GITHUB_REPOSITORY/issues/$pr/comments" -f body="$body" >/dev/null
  echo "Commentaire créé."
fi
