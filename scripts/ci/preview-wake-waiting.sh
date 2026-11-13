#!/usr/bin/env bash
#
# preview-wake-waiting.sh — réveille les PR en attente d'une place de preview (issue #360).
#
# Quand le plafond de previews du homelab est atteint, `preview.yml` ne fait pas échouer la PR : il la
# marque « en attente » (commentaire unique portant le marqueur ci-dessous). Ce script est appelé quand
# une place vient de se libérer (fermeture d'une PR avec preview, ou ramassage du reaper) : il relance
# `preview.yml` en `workflow_dispatch` pour les PR en attente les plus anciennes, une par place libre.
#
#   scripts/ci/preview-wake-waiting.sh --slots <n> [--dry-run]
#
# Une PR est « en attente » si elle est ouverte, du même dépôt, pas Dependabot, sans label `preview`, et
# si son commentaire de preview porte le marqueur d'attente. Le marqueur (et non « pas de label ») évite de
# relancer une PR dont le déploiement a échoué pour une raison de code : elle a besoin d'un correctif,
# pas d'une place.
#
# `--slots` est le nombre de places libérées, connu de l'appelant (le script tourne sur un runner
# hébergé et ne voit pas la LXC). Le plafond reste vérifié par `preview.yml` lui-même : une relance en trop
# (deux PR se disputant la dernière place) se termine « en attente », sans échec.
#
# Aucune nouvelle voie d'exécution sur le runner de la LXC : le run relancé repasse par `resolve`
# (éligibilité même dépôt), sur le fichier de la branche par défaut.
#
# Environnement : GH_TOKEN (permissions `actions: write`, `pull-requests: read`), GH_REPO ou
# GITHUB_REPOSITORY, DEFAULT_BRANCH (défaut : main).
set -euo pipefail

readonly WAITING_MARKER='<!-- houseflow-preview-waiting -->'
readonly WORKFLOW='preview.yml'

SLOTS=""
DRY_RUN=false
while [[ $# -gt 0 ]]; do
  case "$1" in
    --slots)   SLOTS="${2:?--slots attend une valeur}"; shift 2 ;;
    --dry-run) DRY_RUN=true; shift ;;
    -h|--help) sed -n '2,/^set -euo/p' "$0" | sed '$d'; exit 0 ;;
    *) echo "Argument inconnu : $1" >&2; exit 2 ;;
  esac
done

if ! [[ "$SLOTS" =~ ^[0-9]+$ ]]; then
  echo "::error::--slots attend un entier (reçu : « $SLOTS »)" >&2
  exit 2
fi
if [[ "$SLOTS" -eq 0 ]]; then
  echo "Aucune place libérée : rien à relancer."
  exit 0
fi

REPO="${GH_REPO:-${GITHUB_REPOSITORY:-}}"
if [[ -z "$REPO" ]]; then
  REPO="$(gh repo view --json nameWithOwner --jq .nameWithOwner)"
fi
BRANCH="${DEFAULT_BRANCH:-main}"

# Candidates, de la plus ancienne à la plus récente.
# shellcheck disable=SC2016 # programme jq
candidates=$(gh pr list --repo "$REPO" --state open --limit 100 \
  --json number,labels,author,createdAt,isCrossRepository \
  --jq 'map(select(.isCrossRepository | not)
            | select(.author.login | test("dependabot") | not)
            | select(any(.labels[]; .name == "preview") | not))
        | sort_by(.createdAt) | .[].number')

woken=0
for n in $candidates; do
  [[ "$woken" -lt "$SLOTS" ]] || break
  # Un seul commentaire de preview par PR, posé par `report` (github-actions).
  waiting=$(gh api --paginate "repos/$REPO/issues/$n/comments" \
    --jq ".[] | select(.user.login == \"github-actions[bot]\") | select(.body | contains(\"$WAITING_MARKER\")) | .id")
  [[ -n "$waiting" ]] || continue
  if [[ "$DRY_RUN" == "true" ]]; then
    echo "[dry-run] relancerait $WORKFLOW pour la PR #$n"
  else
    echo "Relance de $WORKFLOW pour la PR #$n (en attente d'une place)."
    gh workflow run "$WORKFLOW" --repo "$REPO" --ref "$BRANCH" -f "pr=$n"
  fi
  woken=$((woken + 1))
done

echo "PR relancées : $woken (places libérées : $SLOTS)."
