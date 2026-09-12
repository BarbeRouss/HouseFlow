# Lessons Learned

Patterns et erreurs à éviter, capturés après corrections.

---

## 2026-03-12

### Sprint créé sans User Story correspondante
**Contexte:** Feature "Prochaines tâches" ajoutée directement dans sprint.md sans être dans user-stories.md.
**Cause:** Workflow incomplet - on a sauté l'étape d'ajout aux specs.
**Leçon:** TOUJOURS ajouter une US dans `specs/user-stories.md` AVANT de créer un sprint. Le sprint référence les US, pas l'inverse.

### Tests InMemory ne détectent pas les migrations manquantes
**Contexte:** L'API refusait de démarrer avec PendingModelChangesWarning, mais les tests passaient.
**Cause:** Les tests d'intégration utilisent `UseEnvironment("Testing")` avec base InMemory qui ne vérifie pas les migrations.
**Leçon:** Toujours vérifier que les migrations sont à jour avant de démarrer Aspire. Commande: `dotnet ef migrations list`.

### Port 22222 occupé après arrêt brutal d'Aspire
**Contexte:** Aspire refuse de démarrer car le port 22222 est occupé.
**Cause:** Le processus DCP d'Aspire n'a pas été arrêté proprement.
**Leçon:** Tuer le processus manuellement: `netstat -ano | findstr :22222` puis `taskkill /PID <PID> /F`.

---

## 2026-03-15

### Toujours tester les commandes Docker/build en local avant de push en CI
**Contexte:** Multiples itérations (6+) pour débugger le deploy CI sans pouvoir voir les logs.
**Cause:** Les commandes Docker et dotnet publish n'ont pas été testées localement d'abord. Chaque fix nécessitait un push + 5 min d'attente.
**Leçon:** TOUJOURS tester les commandes de build en local avant de les mettre dans le CI. Si Docker n'est pas dispo localement, au minimum valider `dotnet restore`, `dotnet build`, `npm run build`, et vérifier l'existence des fichiers référencés.

### GHCR exige des noms d'images en minuscules
**Contexte:** `docker push ghcr.io/BarbeRouss/...` échouait silencieusement.
**Cause:** `github.repository_owner` peut contenir des majuscules. GHCR refuse les majuscules.
**Leçon:** Toujours passer le owner en minuscules : `echo "$OWNER" | tr '[:upper:]' '[:lower:]'`.

### Ne pas mettre Aspire.Hosting.AppHost dans un projet service
**Contexte:** `dotnet restore` échouait dans le Dockerfile de l'API.
**Cause:** `Aspire.Hosting.AppHost` nécessite le workload Aspire, non disponible dans l'image Docker SDK standard.
**Leçon:** Ce package appartient au AppHost uniquement. Les projets service utilisent les packages client (ex: `Aspire.Npgsql.EntityFrameworkCore.PostgreSQL`).

### Vérifier l'existence des fichiers/dossiers référencés dans un Dockerfile
**Contexte:** `COPY --from=build /app/public ./public` échouait car le dossier n'existait pas.
**Cause:** Le Dockerfile a été écrit en supposant l'existence d'un dossier `public/`.
**Leçon:** Toujours vérifier avec `ls` que les fichiers/dossiers existent avant de les référencer dans un Dockerfile.

---

## 2026-03-18

### Toujours créer un test E2E pour les bugs de comportement remontés par l'utilisateur
**Contexte:** Bug "back navigateur après inscription ramène à la page register" corrigé sans test E2E initialement.
**Cause:** Le réflexe de créer un test de non-régression n'était pas systématique.
**Leçon:** Quand l'utilisateur remonte un bug de comportement (UX, navigation, redirections, etc.), TOUJOURS créer un test E2E Playwright qui reproduit le scénario et valide la correction. Le test doit être ajouté dans le même commit ou immédiatement après le fix.

### Accès à l'API GitHub : utiliser `gh` CLI, pas `curl` sur le proxy Git
**Contexte:** Tentative d'accéder aux commentaires de PR via `curl` sur le proxy local (`127.0.0.1:<port>/api/v1/...`) → `400 Invalid path format`.
**Cause:** Le proxy Git local n'expose que le protocole Git smart HTTP (`/git/...` → `info/refs`, `git-upload-pack`, `git-receive-pack`). Il ne proxifie PAS l'API REST GitHub/Gitea. De plus le port du proxy est dynamique et change entre les sessions.
**Leçon:** TOUJOURS utiliser `gh` CLI pour interagir avec l'API GitHub (PRs, issues, commentaires, checks, reviews). Exemples :
- `gh api repos/OWNER/REPO/pulls/N/comments` → commentaires de review
- `gh pr checks N` → statut CI
- `gh pr view N` → détails PR
- Le proxy local sert uniquement pour `git fetch/push/clone`. Ne jamais tenter `curl` dessus pour l'API REST.

### Toujours exécuter le script d'initialisation avant les tests d'intégration
**Contexte:** Tests d'intégration (Testcontainers) échouaient tous (144/144) car Docker n'était pas démarré.
**Cause:** Le script `scripts/init-session.sh` n'a pas été exécuté en début de session. Il démarre Docker, PostgreSQL, et installe les dépendances.
**Leçon:** TOUJOURS exécuter `bash scripts/init-session.sh` en début de session web avant de lancer les tests. Ne pas conclure "Docker n'est pas disponible" sans avoir d'abord cherché un script d'initialisation.

---

## 2026-03-23

### TOUJOURS vérifier les tests ET attendre la fin des checks CI après un commit
**Contexte:** Des tests cassaient en CI sans avoir été lancés localement avant le push.
**Cause:** Le build passait localement, mais la suite de tests complète n'avait pas été exécutée avant le push.
**Leçon:** TOUJOURS avant de push, exécuter la checklist complète (cf. CLAUDE.md) :
1. `dotnet test` (backend + tests d'intégration)
2. `dotnet build src/HouseFlow.Web` (build du frontend Blazor WASM)
3. `cd src/HouseFlow.Web && npm run build:css` (CSS Tailwind)
4. `bash scripts/verify-e2e.sh` (E2E Playwright)
5. Après le push, vérifier les checks CI avec `gh pr checks` et attendre qu'ils soient tous verts
6. Ne jamais considérer une tâche comme terminée tant que les checks CI ne sont pas passés

### Toujours valider le build CI après chaque push — itérer si échec

**Contexte:** Des builds CI échouaient sans qu'on s'en rende compte, causant des retours tardifs et des itérations coûteuses.
**Cause:** Le push était considéré comme "terminé" sans vérifier le résultat du workflow.
**Leçon:** Après chaque `git push`, TOUJOURS vérifier que le build CI passe. Si échec, itérer jusqu'à ce que ça passe.

#### Procédure post-push obligatoire

```bash
# 1. Attendre que le workflow démarre (quelques secondes après le push)
#    Lister les runs récents pour trouver celui déclenché par notre push
gh run list --repo BarbeRouss/HouseFlow --branch <branch-name> --limit 3

# 2. Surveiller le run en cours (attente bloquante jusqu'à complétion)
gh run watch <run-id> --repo BarbeRouss/HouseFlow

# 3. Si le run échoue, consulter les logs pour identifier l'erreur
#    --failed filtre uniquement les étapes en échec (évite le bruit)
gh run view <run-id> --repo BarbeRouss/HouseFlow --log-failed

# 4. Si besoin de plus de contexte, voir les logs complets d'un job spécifique
gh run view <run-id> --repo BarbeRouss/HouseFlow --log

# 5. Corriger, commit, push, et recommencer à l'étape 1
```

#### Commandes GH utiles pour le debug CI

```bash
# Lister les 5 derniers runs (tous workflows)
gh run list --repo BarbeRouss/HouseFlow --limit 5

# Lister les runs d'un workflow spécifique
gh run list --repo BarbeRouss/HouseFlow --workflow "PR Checks" --limit 5
gh run list --repo BarbeRouss/HouseFlow --workflow "Deploy" --limit 5

# Voir le résumé d'un run (jobs, statuts, durées)
gh run view <run-id> --repo BarbeRouss/HouseFlow

# Voir uniquement les logs des étapes échouées (LE PLUS UTILE)
gh run view <run-id> --repo BarbeRouss/HouseFlow --log-failed

# Voir les logs complets (verbose, beaucoup de sortie)
gh run view <run-id> --repo BarbeRouss/HouseFlow --log

# Relancer un run échoué sans re-push
gh run rerun <run-id> --repo BarbeRouss/HouseFlow

# Relancer uniquement les jobs échoués
gh run rerun <run-id> --repo BarbeRouss/HouseFlow --failed
```

#### Pattern d'itération

1. `git push` → `gh run list` → noter le `<run-id>`
2. `gh run watch <run-id>` → attendre la fin
3. Si **success** → terminé
4. Si **failure** → `gh run view <run-id> --log-failed` → lire l'erreur
5. Corriger le code → commit → push → retour à l'étape 1
6. Répéter jusqu'à ce que le build soit vert

---

## 2026-03-26

### TOUJOURS lancer les tests E2E Playwright avant de push
**Contexte:** Claude Code a cassé les tests Playwright E2E à plusieurs reprises (ex: durcissement CSP) sans jamais les vérifier avant de push. L'utilisateur devait rappeler à chaque fois.
**Cause:** La checklist pre-push dans CLAUDE.md et lessons.md ne mentionnait pas les tests Playwright. Seuls vitest, dotnet test, et next build étaient vérifiés.
**Leçon:** TOUJOURS avant de push, exécuter `bash scripts/verify-e2e.sh` qui :
1. Démarre les services (API + frontend) si nécessaire
2. Lance `npx playwright test --project=chromium`
3. Écrit un marqueur `/tmp/houseflow-e2e-verified` en cas de succès
Un hook PreToolUse bloque `git push` si le marqueur n'existe pas ou date de plus d'1 minute. Les tests E2E détectent des régressions invisibles aux tests unitaires.

---

## 2026-03-29

### Ne jamais mettre deux environnements déployés indépendamment dans le même Terraform state
**Contexte:** Prod et preprod étaient dans le même state (`main.tfstate`). Quand `deploy-preprod` faisait `terraform apply`, il mettait aussi à jour le tag Docker de la prod, contournant la gate d'approbation manuelle.
**Cause:** Une seule variable `api_image_tag` partagée entre les deux envs, et un seul `terraform apply` sur tout le state.
**Leçon:** Séparer les states Terraform par périmètre de déploiement. Chaque environnement déployé indépendamment doit avoir son propre state. Les ressources partagées (VNet, PostgreSQL, CAE) restent dans un state commun, accessible en lecture via `terraform_remote_state`. Pattern : `main/` (infra partagée) + `deploy-prod/` + `deploy-preprod/` + `ephemeral/`.

### Vérifier les management locks lors d'un déplacement de ressources entre states
**Contexte:** Après avoir déplacé les Container Apps de `main/` vers `deploy-prod/`, les `azurerm_management_lock` dans `main/resource-group.tf` référençaient encore `azurerm_container_app.api_prod.id` → `terraform plan` aurait échoué.
**Cause:** Les locks référençant les Container Apps n'ont pas été déplacés avec elles.
**Leçon:** Quand on déplace des ressources entre states Terraform, TOUJOURS vérifier les ressources dépendantes (locks, outputs, locals) qui les référencent.

---

## 2026-03-31

### Un state Terraform partagé pour N environnements éphémères cause des suppressions croisées
**Contexte:** Créer une PR#43 supprimait l'environnement éphémère de PR#42. Le problème était intermittent.
**Cause:** Toutes les PRs partageaient `ephemeral.tfstate` avec `for_each = var.pr_envs`. Mais `pr_envs` ne contenait que la PR courante, donc Terraform voyait les autres comme orphelines. Le lock global sérialisait tout. `cancel-in-progress: true` pouvait tuer un apply en cours. `force-unlock` pouvait corrompre le state d'une autre PR.
**Leçon:** Un state Terraform par environnement déployé indépendamment. Pour les environnements éphémères : `ephemeral-pr-{N}.tfstate` via `-backend-config="key=..."`. Plus de `for_each`, plus de `-target`, plus de lock global, plus de `force-unlock`. Chaque PR est totalement isolée. Même pattern que la séparation prod/preprod (leçon 2026-03-29).

### Retry API : ne pas retenter les requêtes non-idempotentes
**Contexte:** Implémentation du retry automatique pour les appels API (issue #42).
**Cause:** Un POST qui échoue avec un timeout peut avoir été traité côté serveur. Retenter = risque de doublon (double création, double envoi d'invitation, etc.).
**Leçon:** Ne retenter automatiquement que les méthodes idempotentes (GET, PUT, DELETE, HEAD, OPTIONS). Pour POST/PATCH, laisser l'utilisateur décider de réessayer manuellement. Si un endpoint POST est garanti idempotent (ex: clé d'idempotence), on peut opt-in via un header custom.

---

## Template

### [Titre court du problème]
**Contexte:** Qu'est-ce qui s'est passé ?
**Cause:** Pourquoi c'est arrivé ?
**Leçon:** Comment éviter à l'avenir ?

### Blazor WASM : services d'auth partagés doivent être Singleton (pas Scoped)
**Contexte:** Après migration du frontend vers Blazor WebAssembly, les requêtes API partaient sans header `Authorization` quand le token était chargé au boot (rbac E2E : boucle de 401 → refresh, `networkidle` jamais atteint, timeout 60s). Les tests où le token était posé pendant l'exécution (login UI) marchaient.
**Cause:** `IHttpClientFactory` résout le `DelegatingHandler` (et ses dépendances) dans un **scope DI séparé**. Un `TokenStore` enregistré `Scoped` donnait au handler une instance différente de celle des composants → le token écrit par les composants n'était jamais vu par le handler. Idem pour `AuthenticationStateProvider` (le `NotifyChanged` du handler n'atteignait pas les abonnés).
**Leçon:** En Blazor WASM (mono-utilisateur), enregistrer en **Singleton** tout service partagé entre composants et handlers HTTP (`TokenStore`, `AuthenticationStateProvider`, `RedirectGuard`). Ne jamais compter sur `Scoped` pour partager un état avec un message handler.

### Playwright dans le devcontainer : libs système + éviter que pkill tue le shell exec
**Contexte:** Chromium ne démarrait pas (`libglib-2.0.so.0` manquant) ; et gérer les process app via `pkill -f "<motif>"` tuait le shell `feature-env exec` lui-même.
**Cause:** Les deps système Playwright ne sont pas dans l'image de base ; et l'argv du shell `bash -lc '...'` contient le texte du script, donc `pkill -f` matche le motif présent dans ce texte.
**Leçon:** `sudo npx playwright install-deps chromium` (baké dans le Dockerfile) ; mettre les `pkill` dans des **fichiers de script** (`scripts/dev-*.sh`, `scripts/e2e.sh`) et matcher des motifs absents de la ligne de commande de l'exec (ex: `HouseFlow.Web.dll`, pas `HouseFlow.Web`).

### Blazor : Value="_champ" sur un paramètre string passe la chaîne littérale
**Contexte:** Les dropdowns (`HfSelect`) n'affichaient jamais la valeur sélectionnée (le trigger restait sur le placeholder), alors que la sélection fonctionnait (valeur bien soumise à l'API).
**Cause:** `<HfSelect Value="_type" />` — comme le paramètre `Value` est de type `string`, Razor passe la **chaîne littérale** `"_type"` et non la valeur du champ `_type`. `DisplayLabel` ne trouvait donc jamais d'option correspondante.
**Leçon:** Pour lier un champ à un paramètre **string** d'un composant, toujours préfixer par `@` : `Value="@_type"`. (Pour les types non-string, `Value="_type"` est déjà interprété comme une expression — d'où le piège spécifique aux strings.) Les tests E2E qui vérifient seulement le comportement (pas le rendu du trigger) ne détectent pas ce bug → ajouter une assertion sur l'affichage.

### BlazorBlueprint.Icons.Lucide : namespace .Components + noms d'icônes Lucide exacts
**Contexte:** Aucune icône ne s'affichait dans toute l'app (`<LucideIcon>` rendait un DOM vide), puis certaines manquaient encore (ex: "Ma maison", alertes).
**Cause:** (1) `@using BlazorBlueprint.Icons.Lucide` importe un `LucideIcon` no-op du namespace racine ; le vrai composant est dans **`BlazorBlueprint.Icons.Lucide.Components`**. (2) Certains noms n'existent pas dans le set : `alert-triangle` → `triangle-alert`, `home` → `house`, `alert-circle` → `circle-alert`, `logout` → `log-out`.
**Leçon:** Importer `@using BlazorBlueprint.Icons.Lucide.Components`. Utiliser les ids Lucide canoniques kebab-case ; en cas de doute, rendre une page de test avec les noms candidats et vérifier `svg > path` non vide.

---

## 2026-09-11

### Sandbox web : `dotnet` installé mais absent du PATH des sous-shells
**Contexte:** `dotnet test` lancé en arrière-plan échouait avec `dotnet: command not found` alors que le hook d'init annonçait le SDK installé.
**Cause:** Le hook installe le SDK dans `/usr/share/dotnet` et n'exporte le PATH que pour son propre shell.
**Leçon:** En début de session web, vérifier `which dotnet` ; sinon `ln -sf /usr/share/dotnet/dotnet /usr/local/bin/dotnet`. Idem `dotnet-ef` (outil global dans `/root/.dotnet/tools`, à ajouter au PATH).

### E2E en parallèle depuis plusieurs worktrees : ports et base paramétrables
**Contexte:** Quatre sous-agents en worktree devaient valider leurs E2E simultanément ; `dev-api.sh`/`dev-web.sh`/`verify-e2e.sh` étaient figés sur 5203/3000 et la base `houseflow`, et `pkill -f "HouseFlow.API"` tuait l'API des autres worktrees.
**Cause:** Scripts écrits pour un seul environnement (devcontainer par worktree).
**Leçon:** Hors devcontainer, utiliser `POSTGRES_HOST=localhost API_PORT=53xx WEB_PORT=33xx DB_NAME=houseflow_x bash scripts/verify-e2e.sh` (un jeu de ports + une base par worktree ; `FRONTEND_URL` est propagé à Playwright). Les `pkill` ne ciblent plus que le port de la worktree. Toujours réserver 5203/3000 à l'agent principal, et **redémarrer l'API/le front avant la vérification finale** : `verify-e2e.sh` réutilise un serveur déjà démarré (donc potentiellement un binaire périmé).

### RGPD : « J'accepte la politique de confidentialité » n'est pas un consentement Art. 7
**Contexte:** L'issue #135 demandait une case « j'ai lu et j'accepte la politique de confidentialité et les CGU » présentée comme un consentement.
**Cause:** Confusion fréquente entre base légale contractuelle (Art. 6(1)(b)) et consentement (Art. 6(1)(a)/7) ; l'EDPB (LD 05/2020) interdit le consentement groupé avec les CGU et un consentement non refusable n'est pas libre.
**Leçon:** Pour un traitement nécessaire au service : case « J'accepte les CGU » (contrat) + mention de prise de connaissance de la politique (information Art. 13), jamais « je consens au traitement ». Réserver une case séparée, optionnelle, à toute finalité facultative (newsletter). Toujours vérifier les règles auprès des sources primaires (CNIL/EDPB) avant d'implémenter une exigence juridique décrite dans une issue.

### Audit indépendant après fusion : les bugs se cachent aux coutures entre agents
**Contexte:** Un agent auditeur (lecture seule, checklist de 86 points) a trouvé après fusion un vrai bug RGPD : les entrées d'audit écrites à l'inscription portaient `UserId = null` (l'identifiant n'était posé dans le contexte d'audit qu'après) et échappaient donc à l'anonymisation à la suppression du compte — l'email survivait un an. Aucun agent d'implémentation ne pouvait le voir : l'un écrivait l'audit à l'inscription, l'autre l'anonymisait à la suppression.
**Cause:** Découpage par feature ; chaque agent a testé son périmètre, pas l'invariant transversal (« plus aucune trace identifiante après suppression »).
**Leçon:** Après une fusion multi-agents, lancer systématiquement un audit indépendant contre une checklist externe, et ajouter des tests d'invariants transversaux (ex. `AuditLogs` sans l'email après `DELETE /users/me`). Poser le contexte d'audit avec l'identifiant dès qu'il est connu (avant le premier `SaveChanges`).

### Serveur de dev Blazor périmé après un build → E2E bloqués à « Chargement… 0 % »
**Contexte:** Après `dotnet build src/HouseFlow.Web` (étape 2 de la checklist) puis `verify-e2e.sh`, tous les tests échouaient en timeout de 60 s sur la page d'inscription ; le diagnostic initial (« machine saturée par les agents parallèles ») était faux.
**Cause:** Le build régénère les assets `_framework` avec une nouvelle empreinte (`dotnet.<hash>.js`) ; le serveur de dev démarré avant le build sert toujours l'ancien manifeste → 404 sur le runtime, l'app WASM ne démarre jamais. `verify-e2e.sh` réutilisait le serveur « déjà en cours ».
**Leçon:** `verify-e2e.sh` redémarre désormais TOUJOURS l'API et le front. Pour diagnostiquer un boot WASM, ouvrir la page dans un Chromium headless et logger les réponses ≥ 400 (`page.on('response')`) avant d'accuser la charge machine. Ne jamais éditer un script bash pendant qu'il s'exécute (bash le lit au fil de l'eau).

---

## 2026-09-12

### Les images Docker doivent être construites en PR, pas seulement au deploy
**Contexte:** Après la migration Blazor, `deploy.yml` construisait toujours l'image frontend depuis `src/HouseFlow.Frontend` (supprimé). Aucune PR ne l'a détecté : `pr.yml` ne construisait pas les images. Toutes les mises en production ont échoué pendant des semaines (issue #155).
**Cause:** La chaîne de déploiement n'était exercée que sur `main`, après merge. Un pivot de stack (Next.js → Blazor) a nettoyé le code applicatif mais pas les Dockerfiles/workflows/Terraform qui le référençaient.
**Leçon:** Tout ce que `deploy.yml` construit doit aussi être construit dans `pr.yml` (job `docker-images`, sans push, avec un smoke test). Lors d'une migration de stack, grep les workflows ET le Terraform pour les chemins/variables de l'ancienne stack (`src/HouseFlow.Frontend`, `NEXT_PUBLIC_*`, ports, sondes).

### Blazor WASM hébergé : publier le client standalone, pas via le projet hôte
**Contexte:** `dotnet publish HouseFlow.WebHost` produisait un `wwwroot/index.html` avec `<script type="importmap"></script>` vide et `blazor.webassembly#[.{fingerprint}].js` non résolu → l'app ne bootait jamais (splash infini).
**Cause:** Les placeholders de fingerprint (`OverrideHtmlAssetPlaceholders`) ne sont résolus que par le publish du projet WASM lui-même ; le publish de l'hôte ne fait que collecter les assets statiques du projet référencé.
**Leçon:** Dans le Dockerfile : `dotnet publish HouseFlow.Web` (standalone) puis overlay de son `wwwroot` sur le publish de `HouseFlow.WebHost`. Toujours vérifier un publish Blazor dans un vrai navigateur (Playwright headless : attendre la disparition de `.hf-splash`), pas seulement avec `curl` — les 200 HTTP ne prouvent pas que le runtime démarre.

### Actions GitHub : un workflow supprimé reste listé tant que ses runs existent
**Contexte:** "CI" (`ci.yml`, ère Next.js) et "Deploy Blazor POC" (`deploy-blazor-poc.yml`, branche de POC supprimée) apparaissaient encore dans l'onglet Actions alors que les fichiers n'existent sur aucune branche.
**Cause:** GitHub garde un workflow visible tant qu'au moins un run lui est rattaché.
**Leçon:** Pour faire disparaître un workflow orphelin, supprimer tous ses runs (UI : workflow → `…` → Delete workflow run, ou `gh api -X DELETE repos/<owner>/<repo>/actions/runs/<id>`). Vérifier aussi que la stack Azure d'un POC a bien été détruite (`terraform destroy`) avant de supprimer son répertoire Terraform.
