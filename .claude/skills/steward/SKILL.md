---
name: steward
description: Conventions HouseFlow pour piloter une PR jusqu'au merge (CI, reviews, conflits). Lu automatiquement par la boucle de livraison (hook Stop scripts/hooks/stop-ship-check.sh) et par les sessions qui surveillent une PR.
---

# Pilotage d'une PR HouseFlow

## Checks à surveiller (tous doivent être verts)
- `PR Checks` (`.github/workflows/pr.yml`) : Backend Build, Backend Unit Tests, Backend Integration Tests, Web Checks (Blazor), E2E Tests (Playwright).
- `PR Preview` (`.github/workflows/pr-preview.yml`) : environnement éphémère Azure — un échec Terraform/déploiement compte aussi.
- `Claude Approvals` s'il est présent : ses lignes bloquantes sont à corriger, pas à reporter.

## Diagnostic
- `gh run list --repo BarbeRouss/HouseFlow --branch <branche> --limit 3` puis `gh run view <run-id> --repo BarbeRouss/HouseFlow --log-failed`.
- GraphQL GitHub est indisponible dans les sessions distantes : rester sur `gh api repos/...` (REST) ou les outils MCP GitHub.
- Avant de corriger, vérifier si le même check est rouge sur `main` (échec non imputable à la PR) : dans ce cas, porter le correctif s'il existe et le dire une fois dans un commentaire de PR.
- « Flaky » n'est pas une cause : un test qui échoue deux fois est cassé. Ne jamais skipper / désactiver un test pour passer au vert.

## Corrections
- Toujours repasser la checklist complète de `CLAUDE.md` (dotnet test, build Web — qui compile aussi la CSS Tailwind —, `scripts/verify-e2e.sh`, en redémarrant `dev-api.sh`/`dev-web.sh` après un build) avant chaque push — le hook pre-push exige un marqueur E2E de moins d'une minute.
- Un commit corrigé vaut mieux qu'un commentaire ; commenter seulement pour expliquer un « non » ou un échec hors périmètre.
- Conflit avec `main` : `git merge origin/main` (jamais de rebase ni de force-push sur une branche partagée), régénérer les fichiers générés avec l'outillage (`scripts/generate-api.sh`, `dotnet build src/HouseFlow.Web` pour la CSS, `dotnet ef migrations`), puis checklist et push.

## Reviews
- Petites demandes (renommage, nit, test manquant, finding de bot vérifié) : corriger, pousser, résoudre le thread.
- Demandes larges (refactor multi-fichiers, changement d'API/schéma, design) : proposer dans le thread, laisser l'auteur / l'utilisateur trancher.
- Après un push qui répond à une review « changes requested », re-demander la review.

## Fin de livraison
- Terminé = PR verte sur le commit de tête, mergeable, aucun thread ouvert. Le merge lui-même reste une décision humaine.
- Si un blocage réel ne dépend pas de l'agent (décision produit, secret manquant, infra), l'expliquer à l'utilisateur puis `touch /tmp/houseflow-ship-blocked` pour que la boucle laisse la session s'arrêter.
