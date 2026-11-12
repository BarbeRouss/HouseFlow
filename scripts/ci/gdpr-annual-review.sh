#!/usr/bin/env bash
# Ouvre la revue RGPD annuelle : une milestone `Revue RGPD <année>` et une issue par
# contrôle du § 6.3 de docs/gdpr/README.md.
#
# Appelé par .github/workflows/gdpr-annual-review.yml, mais écrit pour tourner aussi
# en local : un workflow absent de `main` ne peut pas être déclenché, donc `--dry-run`
# est le seul moyen de vérifier la logique avant la fusion.
#
#   scripts/ci/gdpr-annual-review.sh [--dry-run] [--year AAAA] [--control <gabarit>]
#
# Contrat :
#   - la milestone de l'année est la clé d'idempotence : si elle existe, rien n'est
#     créé (un déclenchement manuel accidentel ne duplique aucune issue) ;
#   - avec `--control`, une seule issue est ouverte, et seulement si la milestone ne
#     porte pas déjà une issue du même titre : c'est le chemin de reprise après un
#     échec en cours de cycle (relancer le contrôle manquant, un par un) ;
#   - tous les gabarits sont lus et validés avant la moindre création, pour qu'une
#     erreur de gabarit ne laisse pas un cycle à moitié ouvert ;
#   - `--dry-run` journalise ce qui serait créé et ne fait que des lectures.
#
# Un gabarit est `.github/gdpr-controls/<controle>.md` : sa première ligne est le titre
# (`# …`), le reste est le corps de l'issue. Deux jetons sont substitués : `{{year}}`
# et `{{blob}}` (préfixe des liens vers les fichiers du dépôt, sur `main`).
#
# Environnement : GH_TOKEN (ou `gh auth login`) ; GH_REPO ou GITHUB_REPOSITORY, à défaut
# le dépôt du répertoire courant.
set -euo pipefail

DRY_RUN=false
YEAR="$(date +%Y)"
CONTROL=""

while [[ $# -gt 0 ]]; do
  case "$1" in
    --dry-run) DRY_RUN=true; shift ;;
    --year)    YEAR="${2:?--year attend une valeur}"; shift 2 ;;
    --control) CONTROL="${2:?--control attend une valeur}"; shift 2 ;;
    -h|--help) sed -n '2,/^set -euo/p' "$0" | sed '$d'; exit 0 ;;
    *) echo "Argument inconnu : $1" >&2; exit 2 ;;
  esac
done

# Labels portés par toutes les issues générées (décision de taxonomie : pas de label
# dédié, c'est la milestone qui distingue ces issues des autres).
LABELS=("type:tech-debt" "security" "priority:medium")

if ! [[ "$YEAR" =~ ^20[0-9]{2}$ ]]; then
  echo "Année invalide : « $YEAR » (attendu : AAAA)." >&2
  exit 2
fi

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
TEMPLATES_DIR="$ROOT/.github/gdpr-controls"
MILESTONE="Revue RGPD $YEAR"

REPO="${GH_REPO:-${GITHUB_REPOSITORY:-}}"
if [[ -z "$REPO" ]]; then
  REPO="$(gh repo view --json nameWithOwner --jq .nameWithOwner)"
fi
BLOB="https://github.com/$REPO/blob/main"

# --- Gabarits : inventaire et validation, avant toute écriture -------------------------

shopt -s nullglob
ALL=("$TEMPLATES_DIR"/*.md)
shopt -u nullglob
if [[ ${#ALL[@]} -eq 0 ]]; then
  echo "Aucun gabarit dans $TEMPLATES_DIR." >&2
  exit 1
fi

SELECTED=()
for f in "${ALL[@]}"; do
  slug="$(basename "$f" .md)"
  if [[ -n "$CONTROL" && "$slug" != "$CONTROL" ]]; then continue; fi
  SELECTED+=("$f")
done
if [[ ${#SELECTED[@]} -eq 0 ]]; then
  echo "Contrôle inconnu : « $CONTROL ». Gabarits disponibles :" >&2
  for f in "${ALL[@]}"; do echo "  - $(basename "$f" .md)" >&2; done
  exit 2
fi

render() { # $1 = fichier ; remplace les jetons
  local s
  s="$(cat "$1")"
  s="${s//\{\{year\}\}/$YEAR}"
  s="${s//\{\{blob\}\}/$BLOB}"
  printf '%s\n' "$s"
}

TITLES=()
BODIES=()
for f in "${SELECTED[@]}"; do
  first="$(head -n 1 "$f")"
  if [[ "$first" != "# "* ]]; then
    echo "Gabarit invalide ($(basename "$f")) : la première ligne doit être un titre « # … »." >&2
    exit 1
  fi
  rendered="$(render "$f")"
  body="$(printf '%s\n' "$rendered" | tail -n +2)"
  if [[ -z "${body//[[:space:]]/}" ]]; then
    echo "Gabarit invalide ($(basename "$f")) : corps vide." >&2
    exit 1
  fi
  TITLES+=("[RGPD $YEAR] ${first#\# }")
  BODIES+=("$body")
done

# --- État de la milestone ----------------------------------------------------------------

existing="$(gh api "repos/$REPO/milestones?state=all&per_page=100" --paginate \
  --jq ".[] | select(.title == \"$MILESTONE\") | .number")"
MILESTONE_EXISTS=false
[[ -n "$existing" ]] && MILESTONE_EXISTS=true

if $MILESTONE_EXISTS && [[ -z "$CONTROL" ]]; then
  echo "La milestone « $MILESTONE » existe déjà (#$existing) : rien à créer."
  exit 0
fi

# Titres déjà présents dans la milestone (mode --control seulement).
declare -A PRESENT=()
if $MILESTONE_EXISTS; then
  while IFS= read -r t; do
    [[ -n "$t" ]] && PRESENT["$t"]=1
  done < <(gh api "repos/$REPO/issues?milestone=$existing&state=all&per_page=100" --paginate --jq '.[].title')
fi

if ! $DRY_RUN; then
  # Un label absent ferait échouer `gh issue create` au milieu du cycle : on le vérifie avant.
  for label in "${LABELS[@]}"; do
    if ! gh api "repos/$REPO/labels/$(printf '%s' "$label" | jq -sRr @uri)" >/dev/null 2>&1; then
      echo "Le label « $label » n'existe pas dans $REPO." >&2
      exit 1
    fi
  done
fi

# --- Création ----------------------------------------------------------------------------

mode="Création"
$DRY_RUN && mode="[dry-run] Créerait"
if $MILESTONE_EXISTS; then
  echo "$mode dans la milestone existante « $MILESTONE » (#$existing), contrôle « $CONTROL » seulement."
else
  echo "$mode la milestone « $MILESTONE » avec ${#SELECTED[@]} issue(s)."
  if ! $DRY_RUN; then
    gh api -X POST "repos/$REPO/milestones" \
      -f title="$MILESTONE" \
      -f description="Contrôles annuels du § 6.3 de docs/gdpr/README.md, ouverts par .github/workflows/gdpr-annual-review.yml. À fermer quand les contrôles de l'année sont tous clos et datés dans le dossier." \
      --jq '"Milestone créée : #\(.number)"'
  fi
fi

label_args=()
for label in "${LABELS[@]}"; do label_args+=(--label "$label"); done

created=0
for i in "${!SELECTED[@]}"; do
  title="${TITLES[$i]}"
  if [[ -n "${PRESENT[$title]:-}" ]]; then
    echo "  = déjà présente, ignorée : $title"
    continue
  fi
  if $DRY_RUN; then
    echo "  + $title   [labels : ${LABELS[*]}]   [corps : $(printf '%s\n' "${BODIES[$i]}" | wc -l) lignes, depuis $(basename "${SELECTED[$i]}")]"
  else
    url="$(gh issue create --title "$title" --body "${BODIES[$i]}" --milestone "$MILESTONE" "${label_args[@]}")"
    echo "  + $title → $url"
  fi
  created=$((created + 1))
done

if $DRY_RUN; then
  echo "[dry-run] $created issue(s) seraient créées ; rien n'a été écrit."
else
  echo "$created issue(s) créée(s) dans « $MILESTONE »."
fi
