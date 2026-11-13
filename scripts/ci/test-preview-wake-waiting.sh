#!/usr/bin/env bash
#
# Test de preview-wake-waiting.sh, hors réseau : un faux `gh` (fixtures JSON synthétiques) remplace
# le vrai, et enregistre les `workflow run` demandés. Lancé par `pr.yml`.

set -euo pipefail

cd "$(dirname "${BASH_SOURCE[0]}")"
script="$PWD/preview-wake-waiting.sh"
tmp=$(mktemp -d)
trap 'rm -rf "$tmp"' EXIT

mkdir "$tmp/bin"
cat >"$tmp/bin/gh" <<'FAKE'
#!/usr/bin/env bash
# Faux gh : `pr list` rend $FAKE_PRS (filtré par --jq comme le vrai), `api …/comments` rend les
# commentaires de $FAKE_COMMENTS_DIR/<n>.json, `workflow run` est consigné dans $FAKE_LOG.
set -euo pipefail
jqf=""
args=("$@")
for ((i = 0; i < ${#args[@]}; i++)); do
  [[ "${args[$i]}" == "--jq" ]] && jqf="${args[$((i + 1))]}"
done
case "$1 $2" in
  "pr list")
    if [[ -n "$jqf" ]]; then jq -r "$jqf" <<<"$FAKE_PRS"; else echo "$FAKE_PRS"; fi ;;
  "api --paginate")
    n=$(sed -E 's#.*/issues/([0-9]+)/comments#\1#' <<<"$3")
    f="$FAKE_COMMENTS_DIR/$n.json"
    [[ -f "$f" ]] || echo '[]' >"$f"
    jq -r "$jqf" "$f" ;;
  "workflow run") echo "$*" >>"$FAKE_LOG" ;;
  *) echo "faux gh : appel inattendu : $*" >&2; exit 99 ;;
esac
FAKE
chmod +x "$tmp/bin/gh"

export PATH="$tmp/bin:$PATH"
export FAKE_COMMENTS_DIR="$tmp/comments" FAKE_LOG="$tmp/log" GITHUB_REPOSITORY=acme/repo
mkdir "$FAKE_COMMENTS_DIR"

# PR synthétiques (plus ancienne d'abord par `createdAt`, volontairement mélangées) :
#   10 en attente, 2e plus ancienne      11 en attente, la plus ancienne     12 en attente mais déjà labellisée
#   13 en attente, fork                  14 en attente, Dependabot           15 pas en attente (échec de code)
#   16 en attente, la plus récente
export FAKE_PRS='[
 {"number":10,"createdAt":"2026-11-02T00:00:00Z","isCrossRepository":false,"author":{"login":"app/houseflow-dev"},"labels":[]},
 {"number":11,"createdAt":"2026-11-01T00:00:00Z","isCrossRepository":false,"author":{"login":"app/houseflow-dev"},"labels":[{"name":"status:in-review"}]},
 {"number":12,"createdAt":"2026-10-30T00:00:00Z","isCrossRepository":false,"author":{"login":"app/houseflow-dev"},"labels":[{"name":"preview"}]},
 {"number":13,"createdAt":"2026-10-29T00:00:00Z","isCrossRepository":true,"author":{"login":"someone"},"labels":[]},
 {"number":14,"createdAt":"2026-10-28T00:00:00Z","isCrossRepository":false,"author":{"login":"app/dependabot"},"labels":[]},
 {"number":15,"createdAt":"2026-10-27T00:00:00Z","isCrossRepository":false,"author":{"login":"app/houseflow-dev"},"labels":[]},
 {"number":16,"createdAt":"2026-11-03T00:00:00Z","isCrossRepository":false,"author":{"login":"BarbeRouss"},"labels":[]}
]'
waiting='[{"id":1,"user":{"login":"github-actions[bot]"},"body":"<!-- houseflow-preview-homelab -->\n<!-- houseflow-preview-waiting -->\nen attente"}]'
for n in 10 11 12 13 14 16; do echo "$waiting" >"$FAKE_COMMENTS_DIR/$n.json"; done
# 15 : commentaire de preview sans marqueur d'attente (déploiement rouge à cause du code).
echo '[{"id":2,"user":{"login":"github-actions[bot]"},"body":"<!-- houseflow-preview-homelab -->\nfailed"}]' >"$FAKE_COMMENTS_DIR/15.json"

failures=0
check() { # <description> <attendu> <réel>
  if [[ "$2" == "$3" ]]; then echo "OK   $1"; else
    echo "FAIL $1 : attendu « $2 », obtenu « $3 »"; failures=$((failures + 1)); fi
}
woken() { # numéros relancés, dans l'ordre, sur une ligne
  [[ -f "$FAKE_LOG" ]] && sed -nE 's/.* -f pr=([0-9]+).*/\1/p' "$FAKE_LOG" | paste -sd, - || true
}

: >"$FAKE_LOG"; "$script" --slots 0 >/dev/null
check "0 place : rien" "" "$(woken)"

: >"$FAKE_LOG"; "$script" --slots 1 >/dev/null
check "1 place : la plus ancienne en attente (ni fork, ni Dependabot, ni labellisée, ni échec de code)" "11" "$(woken)"

: >"$FAKE_LOG"; "$script" --slots 2 >/dev/null
check "2 places : les deux plus anciennes, dans l'ordre" "11,10" "$(woken)"

: >"$FAKE_LOG"; "$script" --slots 9 >/dev/null
check "plus de places que de PR en attente : toutes les éligibles" "11,10,16" "$(woken)"

: >"$FAKE_LOG"; "$script" --slots 2 --dry-run >/dev/null
check "--dry-run : rien n'est lancé" "" "$(woken)"

: >"$FAKE_LOG"; "$script" --slots 1 >/dev/null
check "le workflow est lancé depuis la branche par défaut" "workflow run preview.yml --repo acme/repo --ref main -f pr=11" "$(cat "$FAKE_LOG")"

if "$script" --slots abc >/dev/null 2>&1; then
  echo "FAIL --slots non numérique aurait dû être refusé"; failures=$((failures + 1))
else echo "OK   --slots non numérique refusé"; fi

exit "$((failures > 0))"
