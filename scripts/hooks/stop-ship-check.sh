#!/bin/bash
# Stop hook: boucle de livraison "fin de développement → E2E → commit → push → PR → CI verte".
#
# À chaque fin de tour de l'agent, ce script évalue l'état de la branche et de sa PR et,
# tant que la livraison n'est pas terminée, renvoie l'étape suivante à l'agent (exit 2 =
# la session continue avec le message ci-dessous comme instruction). Il laisse la session
# s'arrêter (exit 0) quand :
#   - la PR est verte, mergeable, sans thread ouvert (terminé) ;
#   - la CI est en cours (le réveil viendra de l'abonnement PR / du check-in planifié) ;
#   - l'agent est réellement bloqué et l'a signalé (marqueur BLOCKED_FILE) ;
#   - la limite d'itérations est atteinte (protection contre les boucles infinies) ;
#   - on est sur main, hors dépôt git, sans remote, ou sans accès GitHub.
#
# Utilise uniquement l'API REST GitHub via `gh api` (GraphQL est indisponible dans les
# sessions Claude Code distantes). Les marqueurs sont remis à zéro par
# user-prompt-reset.sh à chaque message de l'utilisateur.
#
# Les checks surveillés sont ceux qui tournent SUR LA PR : `PR Checks` (pr.yml — Backend
# Build, Backend Unit Tests, Backend Integration Tests, Web Checks, E2E Tests) et
# `PR Preview` (pr-preview.yml). La livraison post-merge est portée par le workflow
# `Pipeline` (pipeline.yml : detect, build, approve-infra, apply-shared, env-prod,
# dbtools-roles, env-preprod, env-preview, certificate, dns, deploy-preprod, approve-prod,
# deploy-prod, lock-prod-db) : il tourne sur `main`, pas sur le commit de tête de la PR,
# et n'entre donc pas dans cette boucle. Conventions : .claude/skills/steward/SKILL.md.

INPUT=$(cat)
if [[ "$(echo "$INPUT" | jq -r '.stop_hook_active // false')" == "true" ]]; then exit 0; fi

BLOCKED_FILE="/tmp/houseflow-ship-blocked"
ITER_FILE="/tmp/houseflow-ship-iterations"
NUDGE_FILE="/tmp/houseflow-ship-gh-nudged"
MAX_ITERATIONS=8

git rev-parse --git-dir >/dev/null 2>&1 || exit 0
[[ -n "$(git remote)" ]] || exit 0
[[ -f "$BLOCKED_FILE" ]] && exit 0

branch=$(git branch --show-current)
[[ -z "$branch" || "$branch" == "main" || "$branch" == "master" ]] && exit 0

# --- exit 2 = l'agent continue avec ce message comme prochaine étape ---
next_step() {
  local n=0
  [[ -f "$ITER_FILE" ]] && n=$(cat "$ITER_FILE")
  n=$((n + 1)); echo "$n" > "$ITER_FILE"
  if (( n > MAX_ITERATIONS )); then
    echo "[ship] Limite de $MAX_ITERATIONS itérations atteinte sans livraison verte. Résume précisément à l'utilisateur ce qui bloque (checks, erreurs, threads), puis écris le marqueur \`touch $BLOCKED_FILE\` avant de terminer ton tour." >&2
    exit 2
  fi
  echo "[ship $n/$MAX_ITERATIONS] $1" >&2
  exit 2
}

# 0. Checklist / E2E / push en cours en arrière-plan : on laisse le tour se terminer,
#    la fin de la tâche réveillera l'agent (notification de tâche en arrière-plan).
if pgrep -f "scripts/verify-e2e.sh" >/dev/null 2>&1 || pgrep -f "playwright test" >/dev/null 2>&1 || pgrep -f "dotnet test" >/dev/null 2>&1; then
  echo "[ship] Checklist (tests/E2E) en cours en arrière-plan sur '$branch'. En attente de sa fin."
  exit 0
fi

# 1. Changements non commités / fichiers non suivis
if ! git diff --quiet || ! git diff --cached --quiet || [[ -n "$(git ls-files --others --exclude-standard)" ]]; then
  next_step "Changements non commités sur '$branch'. Si le développement est terminé : exécute la checklist (dotnet test, dotnet build src/HouseFlow.Web, bash scripts/verify-e2e.sh — redémarre dev-api.sh/dev-web.sh après un build) puis commit. Sinon continue le développement."
fi

# 2. Commits non poussés (ou branche sans remote)
if git rev-parse -q --verify "origin/$branch" >/dev/null 2>&1; then
  unpushed=$(git rev-list "origin/$branch..HEAD" --count 2>/dev/null || echo 0)
else
  unpushed=$(git rev-list HEAD --not --remotes --count 2>/dev/null || echo 1)
fi
if (( unpushed > 0 )); then
  next_step "$unpushed commit(s) non poussé(s) sur '$branch'. Vérifie que scripts/verify-e2e.sh a été exécuté (marqueur < 1 min) puis \`git push -u origin $branch\`."
fi

# 3. Accès GitHub (REST uniquement)
remote_url=$(git remote get-url origin 2>/dev/null)
repo=$(echo "$remote_url" | sed -E 's#^(https://github\.com/|git@github\.com:)##; s#\.git$##')
if ! command -v gh >/dev/null 2>&1 || ! gh api "repos/$repo" --jq .full_name >/dev/null 2>&1; then
  # gh indisponible : un seul rappel (pas de boucle), puis on laisse la session s'arrêter.
  if [[ ! -f "$NUDGE_FILE" ]]; then
    touch "$NUDGE_FILE"
    next_step "gh ne peut pas interroger GitHub ici. Avec les outils GitHub (MCP) : ouvre la PR de '$branch' vers main si elle n'existe pas, abonne-toi à ses événements (subscribe_pr_activity), et suis ses checks jusqu'au vert."
  fi
  exit 0
fi
owner=${repo%%/*}

# 4. PR de la branche
pr_json=$(gh api "repos/$repo/pulls?head=$owner:$branch&state=all&per_page=1" 2>/dev/null)
pr_number=$(echo "$pr_json" | jq -r '.[0].number // empty')
if [[ -z "$pr_number" ]]; then
  next_step "Aucune PR pour '$branch'. Crée la PR vers main (titre clair, description référençant la US / l'item Project / Fixes #XX, footer de la session), puis abonne-toi à ses événements (subscribe_pr_activity) et planifie un check-in (send_later ~1h)."
fi
pr_state=$(echo "$pr_json" | jq -r '.[0].state')
pr_merged=$(echo "$pr_json" | jq -r '.[0].merged_at // empty')
if [[ "$pr_state" == "closed" ]]; then
  [[ -n "$pr_merged" ]] && echo "[ship] PR #$pr_number mergée. Terminé." || echo "[ship] PR #$pr_number fermée sans merge. Terminé."
  exit 0
fi

# 5. Conflit de merge
pr_detail=$(gh api "repos/$repo/pulls/$pr_number" 2>/dev/null)
mergeable=$(echo "$pr_detail" | jq -r '.mergeable // "null"')
mergeable_state=$(echo "$pr_detail" | jq -r '.mergeable_state // "unknown"')
head_sha=$(echo "$pr_detail" | jq -r '.head.sha')
if [[ "$mergeable" == "false" || "$mergeable_state" == "dirty" ]]; then
  next_step "PR #$pr_number en conflit avec main. Merge origin/main dans '$branch' (pas de rebase/force-push), résous, régénère les fichiers générés avec l'outillage du repo, repasse la checklist et pousse."
fi

# 6. Checks CI sur le commit de tête (check-runs + statuts legacy)
checks=$(gh api "repos/$repo/commits/$head_sha/check-runs?per_page=100" 2>/dev/null)
statuses=$(gh api "repos/$repo/commits/$head_sha/status" 2>/dev/null)
failed=$(echo "$checks" | jq -r '.check_runs[] | select(.status=="completed" and (.conclusion|IN("failure","cancelled","timed_out","action_required","startup_failure"))) | .name' 2>/dev/null)
failed_statuses=$(echo "$statuses" | jq -r '.statuses[]? | select(.state|IN("failure","error")) | .context' 2>/dev/null)
pending=$(echo "$checks" | jq -r '.check_runs[] | select(.status!="completed") | .name' 2>/dev/null)
pending_statuses=$(echo "$statuses" | jq -r '.statuses[]? | select(.state=="pending") | .context' 2>/dev/null)
all_failed=$(printf '%s\n%s' "$failed" "$failed_statuses" | sed '/^$/d' | sort -u | paste -sd ',' - | sed 's/,/, /g')
all_pending=$(printf '%s\n%s' "$pending" "$pending_statuses" | sed '/^$/d' | sort -u | paste -sd ',' - | sed 's/,/, /g')

if [[ -n "$all_failed" ]]; then
  run_hint="gh run list --repo $repo --branch $branch --limit 3 ; gh run view <run-id> --repo $repo --log-failed"
  next_step "PR #$pr_number : check(s) rouge(s) → $all_failed. Diagnostique ($run_hint), vérifie d'abord qu'il ne s'agit pas d'un échec déjà rouge sur main, corrige, repasse la checklist complète, pousse. Si l'échec n'est pas imputable à cette PR, commente-le une fois sur la PR."
fi

# 7. Threads de review non résolus (route CCR, puis GraphQL si disponible)
unresolved=""
threads=$(gh api "repos/$repo/pulls/$pr_number/ccr/review_threads" 2>/dev/null) && \
  unresolved=$(echo "$threads" | jq -r '[.. | objects | select(has("isResolved") or has("is_resolved")) | select((.isResolved // .is_resolved) == false)] | length' 2>/dev/null)
if [[ -z "$unresolved" ]]; then
  q="query(\$o:String!,\$r:String!,\$n:Int!){repository(owner:\$o,name:\$r){pullRequest(number:\$n){reviewThreads(first:100){nodes{isResolved}}}}}"
  unresolved=$(gh api graphql -f query="$q" -f o="$owner" -f r="${repo#*/}" -F n="$pr_number" --jq '[.data.repository.pullRequest.reviewThreads.nodes[] | select(.isResolved==false)] | length' 2>/dev/null)
fi
if [[ -n "$unresolved" && "$unresolved" != "0" ]]; then
  next_step "PR #$pr_number : $unresolved thread(s) de review non résolu(s). Corrige (petites demandes, findings de bots) ou réponds (propositions plus larges), puis résous les threads traités."
fi

# 8. Review "changes requested" (dernier avis par reviewer)
changes_requested=$(gh api "repos/$repo/pulls/$pr_number/reviews?per_page=100" --jq '[group_by(.user.login)[] | max_by(.submitted_at) | select(.state=="CHANGES_REQUESTED") | .user.login] | join(", ")' 2>/dev/null)
if [[ -n "$changes_requested" ]]; then
  next_step "PR #$pr_number : modifications demandées par $changes_requested. Traite la review, pousse, puis re-demande la review."
fi

# 9. CI en cours : on laisse la session s'arrêter, le réveil viendra des événements PR.
if [[ -n "$all_pending" ]]; then
  echo "[ship] PR #$pr_number : checks en cours ($all_pending). En attente des événements PR."
  exit 0
fi

echo "[ship] PR #$pr_number verte et mergeable, aucun thread ouvert. Livraison terminée."
rm -f "$ITER_FILE"
exit 0
