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

## 2026-09-12

### Fin de développement = PR ouverte + CI surveillée, sans attendre la demande
**Contexte:** Feature admin (US-400) livrée, vérifiée et poussée, mais aucune PR n'a été ouverte ni la CI suivie ; l'utilisateur a dû le demander.
**Cause:** Réflexe « ne pas créer de PR sans demande explicite » appliqué alors que le workflow projet (CLAUDE.md, Phase 2 étape 4) prévoit la PR comme livraison.
**Leçon:** Quand la checklist des 4 étapes est verte et le commit poussé, ouvrir la PR immédiatement, s'abonner à ses événements et suivre TOUS les checks CI jusqu'au vert (corriger et repousser à chaque rouge). Voir CLAUDE.md « Phase 3 ».

### Ne pas recompiler HouseFlow.Web pendant que le devserver Blazor le sert
**Contexte:** Après `dotnet build src/HouseFlow.Web` (étape 2 de la checklist) exécuté alors que `scripts/dev-web.sh` tournait déjà, toute la suite E2E a expiré : la page restait sur « Chargement… 0% » (boot WASM cassé, fichiers `_framework` remplacés sous le devserver).
**Cause:** Le devserver sert `bin/Debug/.../wwwroot` ; un build concurrent change les assets et `blazor.boot.json` en plein run.
**Leçon:** Après un `dotnet build src/HouseFlow.Web` (ou tout changement `.razor`/`.cs` du frontend), redémarrer le devserver (`bash scripts/dev-web.sh start && bash scripts/dev-web.sh wait`) AVANT `scripts/verify-e2e.sh`. Idem pour l'API : `bash scripts/dev-api.sh start` après un changement backend. `verify-e2e.sh` ne redémarre pas un service déjà en ligne. Et ne jamais lancer `pkill -f "playwright test"` depuis un shell dont l'argv contient ce motif (il se tue lui-même — cf. leçon devcontainer).

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
