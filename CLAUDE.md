## Workflow orchestration

### 1. Plan Mode Default
- Enter plan mode for ANY non-trivial task (3+ steps or architectural decisions)
- If something goes sideways, STOP and re-plan immediately — don’t keep pushing
- Use plan mode for verification steps, not just building
- Write detailed specs upfront to reduce ambiguity

### 2. Subagent Strategy
- Use subagents liberally to keep the main context window clean
- Offload research, exploration, and parallel analysis to subagents
- For complex problems, throw more compute at it via subagents
- One task per subagent for focused execution

### 3. Self-Improvement Loop
- After ANY correction from the user, update `tasks/lessons.md` with the pattern
- Write rules for yourself that prevent the same mistake
- Ruthlessly iterate on these lessons until the mistake rate drops
- Review lessons at the session start for the relevant project

### 4. Verification Before Done
- Never mark a task complete without proving it works
- Diff behavior between main and your changes when relevant
- Ask yourself: “Would a staff engineer approve this?”
- **Checklist obligatoire avant push (TOUT doit passer) :**
  1. `dotnet test` (backend)
  2. `dotnet build src/HouseFlow.Web` (build du frontend Blazor WebAssembly — compile aussi la CSS Tailwind via le target MSBuild `BuildTailwindCss`, plus besoin d'un `npm run build:css` séparé)
  3. `bash scripts/verify-e2e.sh` (E2E Playwright — démarre l'API + le frontend Blazor si nécessaire)
- **Ne JAMAIS push sans avoir exécuté les 3 étapes.** Un hook PreToolUse bloque le push si l'étape 3 (E2E) n'a pas été faite dans la dernière minute.
- Les tests E2E détectent des régressions invisibles aux tests unitaires (routing, intégration API, flows UI complets).
- Passe TOUJOURS par le devcontainer pour ces étapes plutôt que d'installer/lancer les dépendances directement sur la machine — dépendances garanties cohérentes, aucun risque de conflit avec une autre feature en cours. Depuis une worktree : `scripts/feature-env.sh up <nom>` puis `scripts/feature-env.sh exec <nom> -- <commande>` pour chacune de ces étapes (voir section 7 et `.devcontainer/README.md`).

### 5. Demand Elegance (Balanced)
- For non-trivial changes: pause and ask, “Is there a more elegant way?”
- If a fix feels hacky: “Knowing everything I know now, implement the elegant solution.”
- Skip this for simple, obvious fixes — don’t over-engineer
- Challenge your own work before presenting it

### 6. Autonomous Bug Fixing
- When given a bug report: just fix it. Don’t ask for hand-holding
- Point at logs, errors, failing tests — then resolve them
- Zero context switching is required from the user
- Go fix failing CI tests without being told how

### 7. Parallel Work — Worktree Isolation
- Quand une tâche se découpe en sous-tâches indépendantes et non-conflictuelles (plusieurs bugs, plusieurs modules, exploration + implémentation), spawn des subagents avec isolation en worktree (`isolation: worktree`) pour que chacun travaille sur sa propre branche sans jamais toucher aux fichiers de la session principale
- Ne demande pas confirmation avant de spawner ces subagents — fais-le et rapporte les résultats
- Une fois qu'un subagent a terminé et que son travail est propre (tests OK), merge sa branche dans la branche courante puis nettoie son worktree
- N'utilise pas cette isolation pour des tâches séquentielles ou qui touchent les mêmes fichiers — le worktree n'a d'intérêt que pour du travail réellement parallèle
- Pour lancer/valider l'app depuis une worktree, utilise `scripts/feature-env.sh up <nom-worktree>` — chaque worktree obtient son propre conteneur (app + postgres dédiés, ports hôte assignés automatiquement par Docker), aucune convention de port ou de nom de base à respecter, aucun conflit possible avec une autre worktree déjà en cours
- Les URLs (port hôte) ne sont pas stables d'un démarrage à l'autre : toujours les récupérer via `scripts/feature-env.sh url <nom-worktree>`, jamais mémoriser une valeur précédente
- Pour exécuter une commande dans le conteneur d'une worktree (ex: `verify-e2e.sh`, `dotnet test`), utilise `scripts/feature-env.sh exec <nom-worktree> -- <commande>` — à l'intérieur, les ports restent toujours 3000/5203
- `dotnet test` tourne aussi dans le devcontainer (via `POSTGRES_HOST`, base `houseflow_test` dédiée sur le même sidecar, remise à zéro à chaque run par `IntegrationTestFixture`) — pas besoin d'accès Docker à l'intérieur du conteneur pour ça

## Project Structure

```
specs/                  # Spécifications durables (le QUOI, pas le suivi)
├── requirements.md     # Cahier des charges
├── architecture.md     # Architecture technique
├── openapi.yaml        # Contrat API (source de vérité de l'API)
└── ux/                 # Maquettes HTML des écrans

tasks/                  # Connaissances de projet
└── lessons.md          # Leçons apprises (patterns, erreurs à éviter)
```

## Task Management → GitHub Issues

**Une seule source de vérité : l'issue GitHub.** Pas de GitHub Project, pas de fichier de
backlog dans le repo, pas de suivi dans `specs/`. Ce qui n'est pas dans une issue n'existe pas.

### L'issue est auto-documentée
Une issue doit se suffire à elle-même : quelqu'un qui la lit six mois plus tard, sans le
contexte de la conversation qui l'a fait naître, doit pouvoir l'implémenter. Concrètement :

- **Contexte** — le problème utilisateur, formulé « En tant que… je veux… afin de… »
- **État actuel** — ce que fait l'app aujourd'hui, avec les fichiers et écrans concernés
- **Critères d'acceptation** — cases à cocher vérifiables, groupées par couche
  (Frontend / Backend / Tests). C'est la définition de « terminé »
- **Notes techniques** — pièges connus, impacts sur l'existant, dépendances
- **Priorité** — et le pourquoi de cette priorité

Interdits : renvoyer vers un fil de discussion, écrire « comme discuté », ou mettre le
**statut dans le corps** de l'issue (`**Status:** Terminé`). Le statut, c'est l'état
open/closed de l'issue — jamais du texte.

Les templates `.github/ISSUE_TEMPLATE/` (feature, bug, tech-debt) imposent ce format.
Les issues #132 à #139 (RGPD) sont la référence de qualité attendue.

### Taxonomie
- **Type** (obligatoire, un seul) : `type:feature`, `type:bug`, `type:tech-debt`, `type:docs`
- **Domaine** (0..n) : `backend`, `frontend`, `infra`, `security`
- **Priorité** (obligatoire sur toute issue **ouverte**, une seule) : `priority:high`, `priority:medium`,
  `priority:low`. Les issues d'archive (US déjà livrées, migrées rétroactivement) n'en portent pas :
  une priorité sur du travail terminé ne veut rien dire
- **Milestone** : le lot de travail en cours (ex. `RGPD Compliance`). Une milestone dont
  toutes les issues sont fermées se ferme aussi — on ne laisse pas traîner des milestones vides
- `preview` est réservé aux PRs et posé automatiquement par `pr-preview.yml` — ne pas y toucher
- `claude` démarre une session Claude Code web sur l'issue (`.github/workflows/claude-issue.yml`
  déclenche la routine « Correction issue HouseFlow ») : elle suit ce CLAUDE.md de bout en bout
  (Phase 2), pose ses questions en commentaire si des infos manquent, jusqu'à la PR et la CI
  verte. Répondre à Claude en commentaire sur l'issue redémarre automatiquement une session (pas
  la même conversation, une neuve qui relit l'issue et repart de la réponse). Le label se pose à
  la main, après relecture de l'issue — jamais automatiquement
- **Tout commentaire d'une session automatisée sur une issue** (question, blocage, compte-rendu
  final) commence par la ligne `<!-- claude-routine:auto -->`. C'est l'unique garde-fou
  anti-boucle : la routine commente sous l'identité GitHub du propriétaire du dépôt,
  indiscernable d'une réponse humaine pour `claude-issue.yml`. Sans ce marqueur, le commentaire
  redéclenche une session, qui recommente, qui redéclenche — constaté sur #204 (6 sessions
  consécutives pour le même constat de blocage). Le workflow ignore aussi les commentaires
  portant le footer d'attribution Claude Code, mais c'est un filet de sécurité : le marqueur
  reste à poser explicitement

### Le rôle de `specs/`
`specs/` décrit le produit et l'architecture de façon durable (le QUOI). Il ne porte
**aucun statut d'avancement** : pas de ✅/❌, pas de « en cours », rien qui se périme.
L'avancement vit exclusivement dans les issues.

Les **user stories** ne vivent plus dans `specs/` : les 52 US historiques ont été migrées en
issues le 2026-09-12 (`US-XXX: <titre>` dans le titre, critères d'acceptation dans le corps,
fermées pour celles déjà livrées). Une nouvelle US naît directement en issue — il n'y a plus
de fichier où l'ajouter d'abord.

## Workflow: Réflexion → Développement

```
Discussion  →  Issue auto-documentée  →  PR  →  Code
  (POURQUOI)        (QUOI + DONE)      (LIVRAISON)  (FAIRE)
```

### Phase 1: Réflexion (conversation)
Quand l'utilisateur veut implémenter quelque chose :
1. Lire `specs/requirements.md` et `PROJECT_KNOWLEDGE.md` pour l'existant
2. Proposer une approche technique
3. **Créer l'issue auto-documentée** (format ci-dessus), avec type + domaine + priorité
4. Si la feature change le périmètre produit ou l'architecture, mettre à jour `specs/`
   en conséquence — la spec décrit la cible, l'issue porte la livraison
5. Attendre validation utilisateur

### Phase 2: Développement (agent)
Quand l'utilisateur dit « implémente » ou « go », ou quand une session automatisée est
déclenchée par le label `claude` :
1. Lire l'issue (et ses commentaires) — elle contient tout le nécessaire.
   - **Session interactive** (utilisateur présent) : si une info manque, **compléter l'issue
     d'abord** en la lui demandant, ne pas se rabattre sur la mémoire de la conversation.
   - **Session automatisée** (label `claude`, personne pour répondre en direct) : si une info
     nécessaire manque ou qu'un choix ambigu bloque une implémentation sûre (comportement
     attendu flou, critère d'acceptation incomplet, choix technique non tranché), **poser la
     question en commentaire sur l'issue** (`gh issue comment`, préfixée du marqueur
     `<!-- claude-routine:auto -->` — voir Taxonomie) et **s'arrêter sans coder ni
     pousser** — ne jamais deviner à la place de l'utilisateur. Un run ultérieur relit les
     commentaires et peut repartir d'une réponse donnée entre-temps.
   - **Session automatisée, aussitôt l'issue lue** : renommer la session (outil
     `set_session_title`) au format fixe `[<n>] Issue - <titre de l'issue>` — `<n>` le numéro
     sans `#`, le titre tel quel (ex. `[201] Issue - Redirection après login cassée`). C'est
     ce qui permet de retrouver la session d'une issue dans la liste des sessions, qui sinon
     n'ont ni nom ni état. Toujours ce format, jamais une variante.
2. Créer une branche nommée `claude/issue-<n>-<résumé-court-en-kebab-case>`
   (ex. `claude/issue-201-fix-login-redirect`) — `<n>` est le numéro de l'issue
3. Exécuter critère par critère (TodoWrite pour le suivi en session)
4. Mettre à jour `PROJECT_KNOWLEDGE.md` à la fin
5. PR avec `Closes #XX` dans la description — l'issue se ferme au merge
6. Si le périmètre bouge en cours de route, **éditer l'issue** pour qu'elle reste vraie

### Phase 3: Fin de développement → PR + suivi CI (agent, sans attendre de demande)
Dès qu'un développement est terminé (checklist des 3 étapes verte, commit poussé), **ouvrir la PR
soi-même** — ne pas attendre que l'utilisateur le demande — puis **surveiller l'ensemble de la CI**.
Cette boucle est **imposée par un hook Stop** (`scripts/hooks/stop-ship-check.sh`) : à chaque fin de
tour il évalue l'état de la branche/PR et renvoie l'étape suivante tant que la livraison n'est pas verte
(max 8 itérations ; `touch /tmp/houseflow-ship-blocked` si un blocage réel dépend de l'utilisateur ;
remise à zéro à chaque message utilisateur). Conventions détaillées : `.claude/skills/steward/SKILL.md`.
1. Créer la PR vers `main` (titre clair, description référençant l'issue : `Closes #XX`)
2. S'abonner aux événements de la PR (`subscribe_pr_activity`) et suivre **tous** les checks
   (`PR Checks` : build, unit, integration, web, E2E ; preview PR ; Claude Approvals si présent)
3. Tant qu'un check est rouge ou qu'il y a un conflit : diagnostiquer (`gh run view --log-failed`),
   corriger, repasser la checklist, pousser, et recommencer — un push corrigé vaut mieux qu'un commentaire
4. Traiter les commentaires de review (humains et bots) : corriger ou répondre
5. Ne considérer la tâche terminée que quand la PR est **verte, mergeable et sans thread ouvert**
6. **Session automatisée uniquement** (label `claude`) : poster un commentaire de fin sur
   l'issue (préfixé du marqueur `<!-- claude-routine:auto -->`, comme tout commentaire de session
   automatisée) — lien de la PR, résumé en quelques lignes de ce qui a été fait, état de la CI. C'est
   le principal canal de suivi — quelqu'un doit pouvoir suivre le travail depuis l'issue sans
   ouvrir la session Claude. Garder le reste de la session sobre : peu de narration, l'essentiel
   passe par les commentaires GitHub (questions, blocage, compte-rendu final), pas par la
   conversation

## Task Tracking
- **Tout** (features, bugs, dette, docs) : une issue GitHub, auto-documentée
- Une issue = une unité livrable par une PR. Trop gros pour une PR → découper en plusieurs issues
- TodoWrite pendant le développement pour le suivi en session, marquer terminé immédiatement
- Capturer les leçons dans `tasks/lessons.md` après corrections
- Ne PAS créer de fichiers de tâches locaux (backlog.md, sprint.md, etc.)

## RGPD — Registre des traitements (obligation permanente)
- Toute feature qui introduit une **nouvelle donnée personnelle**, une **nouvelle finalité**, un **nouveau destinataire/sous-traitant** (service externe, SDK, analytics, emailing) ou une **nouvelle durée de conservation** DOIT, dans la même PR : (1) mettre à jour `docs/gdpr/processing-register.md` (fiche concernée + date de mise à jour), (2) mettre à jour la politique de confidentialité (`src/HouseFlow.Web/Features/Legal/`) et incrémenter `GdprPolicy.CurrentPolicyVersion` + `LegalConstants.PolicyVersion` si l'information donnée aux utilisateurs change, (3) vérifier `docs/gdpr/data-retention-policy.md` et le `DataRetentionJob` (purge automatique), (4) ajouter un sous-traitant dans `docs/gdpr/subprocessors.md` (DPA, localisation, transfert).
- Aucun traceur tiers (analytics, session replay, embed) sans bandeau de consentement conforme — voir `docs/gdpr/README.md`.
- Ne jamais logger d'email, d'IP complète, de token ou de mot de passe (Serilog) ; ne jamais recopier un secret dans l'audit trail.
- En cas d'incident de sécurité : suivre `docs/security/breach-notification-procedure.md` (72 h).

## Core Principles
- Simplicity First: Make every change as simple as possible. Impact minimal code.
- No Laziness: Find root causes. No temporary fixes. Senior developer standards.
- Minimal Impact: Changes should only touch what's necessary. Avoid introducing bugs.

## Knowledge Maintenance

### PROJECT_KNOWLEDGE.md
- Living documentation of the project architecture, state, and details
- Update automatically when making significant changes:
  - Database schema changes (migrations)
  - New services or architectural changes
  - Test status changes
  - New configuration or environment setup
  - Bug fixes that reveal important patterns
  - Frontend structure or commands changes
- Keep the "Recent Changes" section current with dated entries
- Update "Last Updated" date when modifying the file

### README.md
- Quick start guide (kept minimal)
- Update only when:
  - Quick start commands change
  - New troubleshooting scenarios are common
  - Test counts change significantly
 