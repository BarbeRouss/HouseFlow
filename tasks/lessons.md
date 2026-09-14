# Lessons Learned

Patterns et erreurs à éviter, capturés après corrections.

---

## 2026-03-12

### Sprint créé sans User Story correspondante
**Contexte:** Feature "Prochaines tâches" ajoutée directement dans sprint.md sans être dans user-stories.md.
**Cause:** Workflow incomplet - on a sauté l'étape d'ajout aux specs.
**Leçon:** TOUJOURS ajouter une US dans `specs/user-stories.md` AVANT de créer un sprint. Le sprint référence les US, pas l'inverse.
> ⚠️ Obsolète depuis le 2026-09-11 : `specs/user-stories.md` et les sprints n'existent plus. La règle survivante est « rien ne se code sans issue auto-documentée » (voir l'entrée du 2026-09-11).

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
**Contexte:** Feature admin (US-400 (#191)) livrée, vérifiée et poussée, mais aucune PR n'a été ouverte ni la CI suivie ; l'utilisateur a dû le demander.
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

---

## 2026-09-11

### Le suivi de tâches vivait à trois endroits, dont deux fantômes
**Contexte:** `CLAUDE.md` déclarait GitHub Projects « source de vérité » pour les features et interdisait d'ouvrir des issues pour autre chose que des bugs. Dans les faits : aucun Project utilisé, 67 issues quasi exclusivement des features, le label `bug` jamais posé une seule fois, et `specs/user-stories.md` tenant un backlog parallèle de 51 US sans statut fiable.
**Cause:** La doctrine a été écrite une fois puis jamais confrontée à la pratique. Personne ne relit une consigne qu'on contourne tous les jours ; l'écart se creuse en silence.
**Leçon:** Quand la pratique dévie de la règle écrite depuis plusieurs semaines, c'est la **règle** qu'il faut corriger, pas la pratique. Et une seule source de vérité par nature d'information : l'avancement vit dans l'état open/closed des issues, jamais dans un fichier du repo ni dans le corps de l'issue (`**Status:** Terminé` est un anti-pattern : il périme dès le lendemain).

### Une issue doit survivre à la perte de son contexte de conversation
**Contexte:** Deux générations d'issues coexistaient : les RGPD (#132-139) — contexte, état actuel, critères d'acceptation cochables par couche, notes techniques — et les anciennes (#58-64) réduites à deux lignes descriptives.
**Cause:** Les secondes ont été créées comme aide-mémoire d'une conversation en cours, pas comme unité de travail autonome.
**Leçon:** Écrire chaque issue pour quelqu'un qui la découvre six mois plus tard sans le fil de discussion. Les templates `.github/ISSUE_TEMPLATE/` imposent ce format — s'ils sont contournés, c'est le signe que l'issue n'est pas mûre, pas que le template est trop lourd.
## 2026-09-12

### Une reconnexion forcée n'est pas une mesure de sécurité si le secret reste sur disque
**Contexte:** L'utilisateur devait se reconnecter à chaque fermeture du navigateur (#164). Le JWT était dans `localStorage` (persistant, lisible par XSS) et seul le profil, sans valeur, était dans `sessionStorage` : la « déconnexion » ne protégeait rien et ajoutait de la friction. Le cookie de refresh de 7 jours n'était jamais exploité au démarrage.
**Cause:** Deux morceaux d'un même état de session stockés dans deux stockages aux durées de vie différentes, et un refresh silencieux conditionné à la présence du morceau le plus fragile.
**Leçon:** Un seul détenteur de la persistance de session : le cookie HttpOnly. L'access token vit en mémoire et se reconstruit au boot via `/auth/refresh`. Quand on ajoute une durée de session longue, ajouter en même temps la rotation + détection de réutilisation (par famille, avec fenêtre de grâce pour les onglets concurrents) — sinon un cookie volé vaut la durée entière.

### Ne jamais conditionner le premier rendu à un aller-retour réseau
**Contexte:** Après #164, la preview PR affichait le loader puis une page blanche. `App.razor` attendait la réponse de `POST /auth/refresh` avant de rendre quoi que ce soit ; l'API de preview a 0 réplica au repos et met ~30 s à démarrer à froid. Tout visiteur, même sans session, regardait une page vide le temps du cold start. Localement (API chaude, 401 en 50 ms) et en E2E, rien n'était visible.
**Cause:** Un appel réseau inconditionnel dans `OnInitializedAsync` du composant racine, avec un rendu vide tant qu'il n'a pas répondu — et un environnement de test qui ne reproduit jamais la latence de l'environnement cible.
**Leçon:** (1) Ne faire l'appel de restauration de session que s'il y a une raison de croire qu'une session existe (indice non sensible en `localStorage`). (2) Pendant une attente réseau au démarrage, afficher le même loader que le splash, jamais un composant vide, et borner l'attente avec un `CancellationToken`. (3) Pour reproduire un bug « ça marche en local », chercher d'abord ce que l'environnement cible a de différent (scale-to-zero, cross-site, rate limiter, publish Release) et le rejouer localement — ici `curl -w %{time_total}` sur l'API de preview a donné la réponse en une commande.

### Playwright : `page.request` partage le cookie jar du navigateur
**Contexte:** Après le passage de la session au cookie HttpOnly (#164), deux tests onboarding expiraient sur la page de login : ils créaient l'utilisateur via `page.request.post('/auth/register')`, le cookie `refreshToken` posé par l'API atterrissait dans le contexte navigateur, et l'app démarrait connectée (redirection vers le dashboard, formulaire de login jamais affiché). Le test quick-check comptait aussi le 401 attendu du refresh au démarrage comme erreur console.
**Cause:** `page.request` est l'`APIRequestContext` du contexte navigateur (cookies partagés) ; la fixture `request` est isolée. Et un appel réseau attendu en 401 est journalisé par Chromium comme `console.error`.
**Leçon:** Pour préparer des données via l'API sans connecter le navigateur, utiliser la fixture `request` (ou `context.clearCookies()` après). Pour connecter le navigateur, poser explicitement le cookie (`addRefreshCookie` dans `e2e/fixtures/auth.ts`). Dans un test qui compte les erreurs console, filtrer par `msg.location().url` les réponses d'erreur attendues plutôt que d'assouplir l'assertion.

### Le workflow projet a changé : issue GitHub d'abord, plus de `specs/user-stories.md`
**Contexte:** Proposé d'ajouter une US dans `specs/user-stories.md` alors que l'utilisateur venait de décider que les issues GitHub sont l'unique source de vérité (règle depuis intégrée à `CLAUDE.md`).
**Cause:** Réponse calée sur l'ancienne version de `CLAUDE.md` chargée en début de session, sans re-vérifier `main` après une longue discussion.
**Leçon:** Avant de proposer un artefact de suivi (US, sprint, item Project), relire la section « Task Management » du `CLAUDE.md` courant sur `main`. Toute nouvelle feature = une issue auto-documentée avec `type:` + domaine + `priority:` ; aucun fichier de suivi dans le repo.

### Session Claude Code web : NE PAS bypasser le devcontainer en buildant sur l'hôte
**Contexte:** Dans une session Claude Code sur le web, un `dotnet build`/`restore` lancé **directement sur l'hôte** réussit sans rien configurer, alors que le même build **dans le devcontainer** échoue (TLS). Tentation de « simplifier » en buildant sur l'hôte.
**Cause:** L'hôte de la session est déjà pré-câblé par l'environnement (`HTTPS_PROXY`, `SSL_CERT_FILE`, `CURL_CA_BUNDLE`, `NODE_EXTRA_CA_CERTS` pointant sur la CA du proxy d'egress) — .NET/npm/curl sortent via le proxy explicite qui gère le TLS. Le devcontainer, conteneur Docker **imbriqué**, n'hérite d'aucune de ces variables, n'a pas la CA, et ne peut pas joindre le proxy (loopback `127.0.0.1` de l'hôte). D'où l'échec côté devcontainer uniquement.
**Leçon:** La règle **« tout passe par le devcontainer »** tient, y compris en session web — c'est elle qui garantit l'isolation des dépendances et le travail parallèle en worktrees. Builder sur l'hôte « parce que ça marche » **contourne** cette garantie et ne doit pas être fait. Le bon correctif est de rendre le devcontainer utilisable derrière le proxy (voir ci-dessous), pas de le court-circuiter.

### Devcontainer derrière un proxy TLS intercepteur (Claude Code web) : installer la CA tôt + gérer l'hôte root
**Contexte:** Le `build` de l'image devcontainer échouait en session web dès la 2ᵉ instruction (`curl … deb.nodesource.com` → `curl failed to verify the legitimacy of the server`), puis à la création d'utilisateur (`exit 8`).
**Cause:** (1) Le proxy d'egress re-termine le TLS avec une CA que le conteneur de build ne connaît pas. (2) En session web l'hôte tourne en **root (uid 0)** ; le Dockerfile tentait de renommer le compte root.
**Leçon:** (1) Installer la CA du proxy dans le trust store **avant tout téléchargement HTTPS** du Dockerfile (`update-ca-certificates`) + `NODE_EXTRA_CA_CERTS` pour Node ; `feature-env.sh` dépose la CA (`/root/.ccr/ca-bundle.crt`) dans le contexte de build, placeholder VIDE sinon → **no-op en local**. (2) Si `id -u` = 0, sauter l'alignement d'utilisateur et rester root dans le conteneur (`USERNAME=root`) pour garder le bind mount `/workspace` inscriptible.
**Cause réelle trouvée le 2026-09-12 (voir l'entrée du jour) :** ce n'était ni le certificat, ni NuGet, ni la révocation TLS — c'était le **runtime .NET preview** de l'image. Sur le runtime publié, `dotnet restore` télécharge depuis nuget.org sans rien changer d'autre. Toute l'analyse « le certificat de l'egress ne porte pas de CRL, donc .NET refuse » était un contresens : .NET valide parfaitement cette chaîne.

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

### Migrer un backlog : cartographier avant de créer, sinon on fabrique des doublons
**Contexte:** Migration des 52 US de `specs/user-stories.md` vers GitHub. Le comptage naïf « quelles US sont citées dans une issue ? » en donnait 31 sans issue — mais 4 d'entre elles (US-050 (#61)/051/140/205) avaient déjà une issue équivalente sous un autre titre, sans le numéro d'US (#61 Locale switcher, #59 Theme toggle, #42 Retry logic, #34 Upload documents).
**Cause:** Chercher une clé (`US-XXX`) au lieu de chercher le sujet. Une issue qui traite exactement la même chose sans citer le numéro reste invisible à ce filtre.
**Leçon:** Avant toute création en masse, faire la table de correspondance sujet par sujet et la faire valider. Pour les recouvrements, rattacher (ajouter la référence à l'issue existante) plutôt que créer : un doublon fermé coûte plus cher qu'un rattachement, il fait croire à deux travaux distincts.

### Une trame d'issue appliquée mécaniquement produit des rubriques vides
**Contexte:** Les issues générées pour US-040 (#183)/041/042 (règles de calcul de score) affichaient une section « Critères d'acceptation » vide : ces US n'en ont jamais eu, elles ne contiennent qu'une formule.
**Cause:** Le script appliquait la trame complète à toutes les US sans vérifier que chaque rubrique avait matière.
**Leçon:** Après une génération en masse, relire le rendu réel de quelques éléments et détecter les rubriques vides automatiquement (`grep -c` sur les cases à cocher). Une rubrique vide dans un modèle donne l'impression d'une information perdue alors qu'il n'y en avait pas.

### Le devcontainer tournait sur un runtime .NET preview — c'était ça, la panne réseau
**Contexte:** En session web, tout `dotnet restore` dans le devcontainer échouait (`NU1301 … RevocationStatusUnknown, OfflineRevocation`), alors que `curl` vers le même hôte répondait 200. Conclusion retenue pendant des semaines, puis reprise par moi : « le certificat de l'egress ne porte ni CRL ni OCSP, NuGet exige une vérification de révocation, c'est insoluble ».
**Cause:** L'image était figée sur `mcr.microsoft.com/dotnet/sdk:10.0-preview` → SDK `10.0.100-preview.7`, runtime `10.0.0-preview.7`, alors que la CI (`dotnet-version: 10.0.x`) et l'hôte tournent sur `10.0.401` / runtime `10.0.12`. Une fois l'image alignée sur le runtime publié, **le même code réseau passe** : `HttpClient` nu renvoie 200 et un restore sans aucun cache télécharge 89 paquets depuis nuget.org.
**Leçon:** Deux choses. (1) Épingler le devcontainer sur la **même version que la CI**, par digest : un SDK divergent ne fait pas que produire des résultats non représentatifs, il apporte ses propres bugs. (2) Quand un symptôme réseau n'apparaît que dans un environnement, comparer d'abord les **runtimes** avant d'accuser l'infrastructure réseau — c'est la variable la moins chère à tester et celle qu'on regarde en dernier.

### Un diagnostic plausible et jamais isolé peut survivre des semaines
**Contexte:** L'explication « NuGet exige la révocation TLS » tenait debout : le message d'erreur parle de révocation, le certificat de l'egress n'a effectivement ni CRL ni OCSP, et `curl` (qui ne vérifie pas la révocation) passait. Tout concordait. Elle était fausse.
**Cause:** Personne — moi compris — n'avait isolé la variable. Deux tests de trente secondes suffisaient à la démonter : un `HttpClient` **nu** échoue pareil (donc NuGet n'est pas en cause), et un callback de validation renvoie `SslPolicyErrors = None` avec une chaîne de 3 éléments valide (donc .NET **fait confiance** à ce certificat).
**Leçon:** Un message d'erreur nomme un symptôme, pas une cause. Avant de bâtir un correctif sur une explication, la réfuter : reproduire avec le composant le plus nu possible, et faire parler la validation plutôt que de lire le message agrégé. Corollaire : un correctif qui contourne (ici, monter le cache NuGet pour éviter le réseau) est le signe qu'on n'a pas trouvé la cause — il aurait laissé le conteneur sans accès réseau pour tout le reste.
