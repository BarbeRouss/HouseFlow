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
- Run tests, check logs, demonstrate correctness

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

## Project Structure

```
specs/                  # Spécifications (source of truth)
├── requirements.md     # Cahier des charges
├── user-stories.md     # User stories avec critères d'acceptation
├── architecture.md     # Architecture technique
├── openapi.yaml        # Contrat API
└── wireframes/         # Maquettes UI

tasks/                  # Gestion de projet
├── backlog.md          # Features non planifiées (roadmap)
├── sprint.md           # Sprint actuel avec tâches
└── archive/            # Sprints terminés
```

## Workflow: Réflexion → Développement

### Règle fondamentale
**TOUJOURS** ajouter une nouvelle feature à `specs/user-stories.md` AVANT de créer un sprint.
Le sprint référence les User Stories, pas l'inverse.

```
specs/user-stories.md  →  tasks/sprint.md  →  Code
     (QUOI)                  (COMMENT)        (FAIRE)
```

### Phase 1: Réflexion (conversation)
Quand l'utilisateur veut implémenter une feature:
1. Lire `specs/requirements.md` et `specs/user-stories.md`
2. Analyser l'existant dans `PROJECT_KNOWLEDGE.md`
3. Proposer une approche technique
4. **Si nouvelle feature**: Ajouter US-XXX à `specs/user-stories.md`
5. Générer les tâches dans `tasks/sprint.md` (référencer les US)
6. Attendre validation utilisateur

### Phase 2: Développement (agent)
Quand l'utilisateur dit "implémente" ou "go":
1. Lire `tasks/sprint.md`
2. Exécuter tâche par tâche
3. Cocher [x] dans sprint.md au fur et à mesure
4. Cocher [x] dans user-stories.md quand critères validés
5. Mettre à jour `PROJECT_KNOWLEDGE.md` à la fin
6. Archiver le sprint dans `tasks/archive/YYYY-MM-description.md`

### Format sprint.md
```markdown
# Sprint: [Nom]

**Objectif:** [Description courte]
**Créé:** YYYY-MM-DD
**Status:** En cours | Terminé

## Tâches

### Backend
- [ ] Tâche 1
- [ ] Tâche 2

### Frontend
- [ ] Tâche 1

### Tests
- [ ] Test 1

## Notes
- Point important
```

## Task Tracking
- Utiliser le TodoWrite tool pendant le développement
- Marquer les tâches terminées immédiatement
- Capturer les leçons dans `tasks/lessons.md` après corrections

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
 