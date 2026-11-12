#!/usr/bin/env bash
#
# check-self-hosted-guard.sh — garde-fou des jobs auto-hébergés (issue #352).
#
# Le dépôt est public et le runner de la LXC `houseflow-preview` est persistant : le code d'une
# PR de fork ne doit pas s'y exécuter. Ce script est un LINT contre l'erreur d'un contributeur honnête :
# sur un `pull_request` de fork, GitHub exécute le workflow (et donc ce garde-fou) dans la version de la
# PR, qu'un fork hostile peut modifier. La barrière contre un fork hostile est le réglage GitHub
# « Require approval for all outside collaborators » (voir deploy/preview/README.md).
# Le script échoue si un workflow :
#
#   1. contient un job auto-hébergé (tout `runs-on` qui n'est pas un runner hébergé par GitHub :
#      `self-hosted`, `houseflow-preview`, une expression `${{ … }}`, …) alors qu'il est déclenché
#      par autre chose que `pull_request`, `workflow_dispatch`, `schedule` ou `push` —
#      en particulier `pull_request_target`, `issue_comment`, `workflow_run`, qui s'exécutent dans
#      le contexte du dépôt de base avec des droits que le contenu d'une PR peut influencer ;
#   2. est déclenché par `pull_request` et contient un job auto-hébergé dont le `if:` ne porte pas
#      la condition de même dépôt `github.event.pull_request.head.repo.full_name == github.repository`
#      (évaluée par GitHub avant toute attribution du job au runner).
#
# `schedule`, `workflow_dispatch` et `push` n'existent que sur le dépôt lui-même, pour des
# porteurs du droit d'écriture : ils n'exigent pas la condition de même dépôt.
#
# usage : check-self-hosted-guard.sh [workflow.yml ...]   (défaut : .github/workflows/*.yml)
# Dépendances : yq (mikefarah, v4) et jq — préinstallés sur les runners GitHub `ubuntu-*`.

set -euo pipefail

readonly SAME_REPO_CONDITION='github.event.pull_request.head.repo.full_name == github.repository'
readonly ALLOWED_TRIGGERS='["pull_request","workflow_dispatch","schedule","push"]'

for tool in yq jq; do
  command -v "$tool" >/dev/null 2>&1 || {
    echo "::error::$tool est requis par $0" >&2
    exit 2
  }
done
yq --version | grep -q mikefarah || {
  echo "::error::yq (mikefarah, v4) est requis par $0" >&2
  exit 2
}

if [ "$#" -gt 0 ]; then
  files=("$@")
else
  files=(.github/workflows/*.yml .github/workflows/*.yaml)
fi

# Pour un workflow, une ligne par violation. Ne sort rien si le workflow est conforme.
# shellcheck disable=SC2016 # programme jq : les `$` sont les siens, pas ceux du shell
violations_jq='
  def triggers: (.on) | if type == "string" then [.]
                        elif type == "array" then .
                        elif type == "object" then keys
                        else [] end;
  def labels: .["runs-on"] as $r
    | if $r == null then []
      elif ($r | type) == "string" then [$r]
      elif ($r | type) == "array" then $r
      elif ($r | type) == "object" then (([$r.labels] | flatten | map(select(. != null))) + (if $r.group then ["group:" + $r.group] else [] end))
      else [] end;
  def hosted: length > 0 and all(.[]; type == "string" and test("^(ubuntu|windows|macos)-[a-z0-9.-]+$"));
  def normalized: (. // "") | tostring | gsub("\\s+"; " ");

  . as $wf
  | ($wf | triggers) as $triggers
  | [ ($wf.jobs // {}) | to_entries[]
      | select(.value["runs-on"] != null and (.value | labels | hosted | not)) ]
  | if length == 0 then empty else
      . as $selfHosted
      | ($triggers - $allowed) as $forbidden
      | (if ($forbidden | length) > 0 then
           "un job auto-hébergé (" + ($selfHosted | map(.key) | join(", ")) + ") dans un workflow déclenché par "
           + ($forbidden | join(", ")) + " : seuls pull_request, workflow_dispatch, schedule et push sont admis"
         else empty end),
        (if ($triggers | index("pull_request")) != null then
           $selfHosted[]
           | select((.value["if"] | normalized | contains($cond)) | not)
           | "le job auto-hébergé « " + .key + " » ne porte pas la condition de même dépôt (if: … " + $cond + " …)"
         else empty end)
    end
'

status=0
checked=0
for file in "${files[@]}"; do
  [ -f "$file" ] || continue
  checked=$((checked + 1))
  json=$(yq -o=json '.' "$file")
  while IFS= read -r violation; do
    [ -n "$violation" ] || continue
    echo "::error file=$file::$violation"
    status=1
  done < <(jq -r --argjson allowed "$ALLOWED_TRIGGERS" --arg cond "$SAME_REPO_CONDITION" "$violations_jq" <<<"$json")
done

if [ "$status" -eq 0 ]; then
  echo "Garde-fou des jobs auto-hébergés : $checked workflow(s) conforme(s)."
fi
exit "$status"
