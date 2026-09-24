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
**Leçon:** Après un `dotnet build src/HouseFlow.Web` (ou tout changement `.razor`/`.cs` du frontend), redémarrer le devserver (`bash scripts/dev-web.sh start && bash scripts/dev-web.sh wait`) AVANT `scripts/verify-e2e.sh`. Idem pour l'API : `bash scripts/dev-api.sh start` après un changement backend. Depuis #203, `verify-e2e.sh` détecte lui-même ce cas et redémarre le devserver — la leçon ayant été oubliée une seconde fois, elle est devenue du code. Deux symptômes distincts, tous deux couverts : un asset `_framework/*.js` fingerprinté référencé par `/` qui répond 404 (index périmé), et `appsettings.json` qui revient **vide en gzip** alors que la réponse non compressée est correcte (assets précompressés régénérés sous le devserver — le navigateur, lui, demande gzip ; `curl` nu ne voit rien). Sonder « comme un navigateur » (`Accept-Encoding`) et valider le contenu, pas seulement le code HTTP. Et ne jamais lancer `pkill -f "playwright test"` depuis un shell dont l'argv contient ce motif (il se tue lui-même — cf. leçon devcontainer).

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

### Une PR en conflit n'a pas de CI : le silence des workflows est un symptôme
**Contexte:** Après plusieurs pushes, aucun run « PR Checks » n'apparaissait sur les nouveaux commits de la PR #163 ; la PR affichait `mergeable_state: dirty` parce que `main` avait avancé deux fois pendant le chantier.
**Cause:** GitHub exécute les workflows `pull_request` sur le commit de merge virtuel ; s'il ne peut pas être créé (conflit), aucun run n'est lancé — sans erreur visible.
**Leçon:** À chaque check-in de surveillance, vérifier `mergeable_state` ET `git merge-tree --write-tree HEAD origin/main` avant de regarder la CI ; fusionner `origin/main` dès qu'un conflit apparaît (jamais de rebase sur une branche partagée), régénérer NSwag si `openapi.yaml` a changé des deux côtés, vérifier `dotnet ef migrations has-pending-model-changes`, puis checklist et push. Consigne ajoutée à `.claude/skills/steward/SKILL.md`.

### Sandbox web : Docker et PostgreSQL ne survivent pas à un redémarrage du conteneur
**Contexte:** Après un redémarrage silencieux du conteneur, les 191 tests d'intégration échouaient en 1 ms (« Container runtime 'docker' appears to be unhealthy ») et le hook d'init n'avait pas été rejoué.
**Cause:** Le daemon Docker et le cluster PostgreSQL démarrés par `scripts/init-session.sh` sont des processus du conteneur ; le redémarrage les tue sans relancer le hook SessionStart.
**Leçon:** Quand toute la suite d'intégration échoue instantanément, vérifier `docker info` et `pg_lsclusters` avant de chercher dans le code ; relancer `dockerd &` et `pg_ctlcluster 16 main start` (ou rejouer `scripts/init-session.sh`).

### `POSTGRES_HOST=localhost` casse maintenant les tests d'intégration
**Contexte:** Après fusion de `main`, les 197 tests d'intégration échouaient en 204 ms avec « Refusing to reset the test database: POSTGRES_HOST is 'localhost', expected 'postgres' ».
**Cause:** `IntegrationTestFixture` a gagné un garde-fou : `POSTGRES_HOST` ne doit désigner que le sidecar du devcontainer, parce que la remise à zéro fait un `DROP DATABASE`. Hors devcontainer, la variable doit rester **non définie** — Aspire démarre alors son propre conteneur PostgreSQL éphémère.
**Leçon:** Dans la sandbox web, lancer `dotnet test` **sans** `POSTGRES_HOST` (Docker doit tourner) ; ne garder `POSTGRES_HOST=postgres` que dans le devcontainer. Un échec instantané de toute la suite avec un message explicite est une précondition d'environnement, pas une régression du code : lire le message avant de suspecter la fusion.

### Hacher un secret en base interdit de le rejouer : les mécanismes qui le relisent doivent changer
**Contexte:** Fusion de la session persistante de `main` (familles de refresh tokens, fenêtre de grâce de 30 s) avec le hachage SHA-256 des refresh tokens de la branche RGPD. `main` renvoyait le **jeton courant** à l'onglet perdant d'une course entre deux onglets ; impossible avec une base qui n'en détient que l'empreinte.
**Cause:** Les deux fonctionnalités sont compatibles sur le papier, mais l'une suppose de pouvoir relire la valeur en clair d'un jeton déjà émis, ce que l'autre rend définitivement impossible.
**Leçon:** Avant de hacher un secret déjà stocké en clair, inventorier **tous** les chemins qui le relisent, pas seulement ceux qui le comparent. Ici la fenêtre de grâce a été conservée en émettant un jeton **frère dans la même famille** plutôt qu'en rejouant le courant : même intention (ne pas déconnecter l'onglet perdant), sans valeur en clair conservée.
---

## 2026-09-22

### Une issue périmée par sa dépendance n'est pas une issue ambiguë
**Contexte:** Session automatisée sur #199 (dump pseudonymisé de la prod vers les previews). Ses critères d'acceptation parlaient d'`id-preprod`, de CAE preprod et d'un job `deploy-preprod`, que #223 venait de supprimer. J'ai conclu « ambigu, trop large », commenté et arrêté. Correction de l'utilisateur : le besoin n'avait jamais changé — un dump nocturne anonymisé, restauré automatiquement à la création d'un environnement de PR — et c'étaient les seules exigences.
**Cause:** J'ai pris la lettre des critères (écrits pour une topologie disparue) pour l'intention. Le code de #223 lui-même disait où aller : `pr.tfvars` annonçait que #199 remplacerait les données de démo, `shared/rbac.tf` et `dbtools/README.md` renvoyaient explicitement à #199.
**Leçon:** Quand une dépendance a changé la topologie, séparer le **besoin** (stable, souvent dans « Contexte ») des **moyens** décrits (périmés). Si le besoin est clair et que le code de la dépendance indique la cible, proposer la transposition dans la nouvelle topologie et réécrire l'issue — ne s'arrêter que si le *besoin* lui-même est flou. Un blocage doit nommer la décision précise qui manque, pas la liste des écarts avec l'ancien texte.

### Deux ressources de même nom dans deux souscriptions : on les confond
**Contexte:** #199 introduisait `id-houseflow-dumps` dans les deux souscriptions (écriture en prod, lecture côté jetable), sur le modèle d'`id-houseflow-cert` et de `rg-houseflow-shared`. Retour de l'utilisateur : il confond à chaque fois la ressource de prod et celle de la souscription jetable.
**Cause:** Le même nom des deux côtés permettait au module `environment` de rester agnostique, mais le nom ne disait plus rien des droits ni de la souscription. L'élégance du code se payait en lisibilité pour l'opérateur, qui manipule ces ressources à la main au bootstrap.
**Leçon:** Nommer une ressource d'après ce qui la distingue (son droit : `-writer`/`-reader`, ou sa souscription), jamais par symétrie. Si le code a besoin d'un choix, le déduire (ici de la permanence, comme le job) plutôt que d'imposer un nom identique.

### Un document de conformité fusionné devient faux en silence
**Contexte:** Fusion de 102 commits de `main` dans la branche RGPD. `main` avait supprimé `scripts/sanitize-pii.sh` au profit de la chaîne `dbtools`, et surtout introduit `preserved_emails` : deux comptes réels traversent la pseudonymisation **intacts** jusque dans les environnements de PR. La fiche TR-07 du registre, écrite avant, affirmait toujours « aucune personne réelle » et décrivait une préproduction disparue. Le merge n'a signalé aucun conflit sur ce fichier : les deux côtés avaient touché des zones différentes.
**Cause:** Git détecte les conflits textuels, pas les contradictions sémantiques. Un document qui *décrit* le code ne conflit pas quand le code change ailleurs — il devient simplement faux. Un registre Art. 30 faux est un manquement, pas une coquille.
**Leçon:** Après toute fusion, relire les documents qui **décrivent** le comportement du système (registre, politique de conservation, procédure de violation, PROJECT_KNOWLEDGE) en cherchant les fichiers supprimés ou renommés par l'autre côté (`git diff --diff-filter=D origin/main...HEAD`) et les fonctionnalités ajoutées qui touchent des données personnelles. Un `grep` sur les noms de scripts disparus est le contrôle le moins cher et il trouve la majorité des cas.

### Une résolution de conflit peut supprimer du contenu sans qu'aucun test ne bronche
**Contexte:** Trois fusions successives de `main` sur `specs/openapi.yaml`. À la dernière, les **cinq chemins RGPD** (`/users/me`, `/users/me/export`, `/users/me/consent`) avaient disparu, ne laissant qu'une bannière de section orpheline et deux bannières concaténées sur une même ligne. Le build, les 323 tests backend et les 66 tests E2E passaient tous.
**Cause:** Les contrôleurs de ces endpoints sont écrits à la main ; seuls les **schémas** viennent de NSwag, et ils avaient survécu. Rien dans la chaîne de vérification ne relie le contrat aux routes réellement exposées, alors que `CLAUDE.md` désigne `openapi.yaml` comme source de vérité de l'API.
**Leçon:** Après toute résolution de conflit sur un fichier de contrat ou de configuration, comparer les **inventaires** avant/après, pas seulement vérifier que ça compile : `grep -c '^  /' specs/openapi.yaml` et `git show <avant>:fichier | grep '^  /'` prennent dix secondes. Plus généralement, un conflit résolu se relit avec `git diff <base>...HEAD -- <fichier>`, jamais au seul feu vert de la CI.

### Adapter un mécanisme de sécurité sans rejouer son scénario d'attaque le neutralise
**Contexte:** La fenêtre de grâce de `main` (course entre deux onglets) renvoyait le **jeton courant** au perdant. Le hachage des jetons rendant cette valeur non rejouable, je l'ai remplacée par l'émission d'un **jeton frère**. Les tests passaient, l'intention fonctionnelle était préservée.
**Cause:** Je n'ai raisonné que sur le cas bénin. Or la détection de réutilisation reposait précisément sur la **convergence** des deux parties vers un même jeton : c'est la collision suivante qui trahit le voleur. Des jetons frères indépendants ne collisionnent jamais, donc un cookie volé donnait une session parallèle permanente et invisible, répétable à volonté.
**Leçon:** Quand on modifie un mécanisme de sécurité, écrire d'abord le **scénario d'attaque** qu'il est censé arrêter, puis vérifier que la nouvelle forme l'arrête encore. Un test vert prouve que le cas nominal marche, jamais que la propriété de sécurité tient. Ici le correctif borne la grâce à un seul usage par jeton parent et fait hériter le frère de l'échéance qu'il double.

### Un document qui décrit le code se périme sans conflit et sans erreur
**Contexte:** Repasse en quatre relectures parallèles sur 116 fichiers. Les écarts les plus graves n'étaient pas des bugs mais des **affirmations fausses** : politique de confidentialité déclarant deux stockages navigateur inexistants et omettant le seul réel, durée de cookie annoncée à 7 jours contre 365 possibles, registre affirmant qu'aucune personne réelle n'est concernée, `SECURITY.md` décrivant une infrastructure de pagination qui n'existe pas.
**Cause:** Toutes datent d'une fusion qui a changé le comportement ailleurs dans l'arbre. Git n'a rien signalé : aucune ligne des documents n'avait été touchée des deux côtés.
**Leçon:** Un document de conformité est du code non compilé. Il faut lui donner des tests là où c'est possible (ici : un test verrouille l'égalité des deux constantes de version de politique, un autre vérifie que chaque page légale cite la version en vigueur) et, à défaut, une relecture dédiée après chaque fusion. Le pire cas n'est pas la coquille, c'est l'affirmation vérifiable et fausse : la durée d'un cookie se contrôle en une requête.
---

## 2026-09-23

### Une liste de ressources dans un workflow est une deuxième source de vérité
**Contexte:** #238 (verrou `ovh-dns-zone` trop large). Première version : un seul state, et un job qui applique « tout sauf le DNS » via 19 `-target` écrits en dur dans `pr-preview.yml` (Terraform n'a pas de `-exclude`). Correction de l'utilisateur : maintenir cette liste à côté du code Terraform n'est pas propre — elle dérive dès qu'une ressource est ajoutée.
**Cause:** J'ai contourné la limite de l'outil (`-target` seulement) au lieu de changer la structure. La séparation voulue (ce qui écrit dans OVH / ce qui n'y écrit pas) était une frontière d'architecture, pas un filtre d'apply.
**Leçon:** Quand un workflow doit appliquer « une partie » d'une racine Terraform, découper la racine selon cette frontière (une racine par verrou/cycle de vie, reliées par `terraform_remote_state`) plutôt que d'énumérer des adresses dans la CI. Chaque job applique une racine entière ; l'ordre vit dans l'enchaînement des racines. Et avant de promettre une option CLI (ici `-exclude`), la vérifier dans `terraform <cmd> -help`.

### Un groupe de concurrence GitHub n'est pas une file d'attente
**Contexte:** Les previews « cancelled » de #233, #235, #237 et #163 : jobs sans runner (`runner_id=0`), annulés à la seconde où une autre preview entrait dans le groupe `ovh-dns-zone`.
**Cause:** Un groupe garde au plus **un** job en cours et **un** en attente ; un nouveau venu annule celui qui attend. `cancel-in-progress: false` ne protège que le job en cours.
**Leçon:** Un verrou `concurrency` ne doit couvrir que la section critique, la plus courte possible — sa durée détermine directement le taux d'annulation. Pour diagnostiquer un « cancelled », comparer l'heure d'annulation avec l'heure de création des runs du même groupe (`gh api …/runs/<id>/jobs`) avant de soupçonner une annulation manuelle ou un quota.

### Un mécanisme du framework existe déjà : le lire avant d'écrire le sien
**Contexte:** #198 (500 sur l'acceptation d'invitation, `40001` Postgres). Première version : une boucle de retry maison dans `AcceptInvitationAsync` (compteur, backoff avec jitter, filtre `SqlState`, rollback « sûr », exception dédiée). Chaque itération de test révélait un nouveau bug de la boucle elle-même. Retour de l'utilisateur : « on prend le canon pour tuer une mouche », se baser sur ce que fait EF Core.
**Cause:** J'ai corrigé le symptôme au niveau du service sans regarder la configuration du `DbContext`. Or Aspire active déjà `EnableRetryOnFailure()` en local/CI, Npgsql classe `40001` comme transitoire (`PostgresException.IsTransient`), et seule la branche production n'avait aucune stratégie de retry. La vraie cause était cet écart d'environnement.
**Leçon:** (1) Avant d'écrire une résilience maison, vérifier ce que le framework et ses intégrations (Aspire, provider) configurent déjà, et comparer les environnements. (2) Pattern canonique EF Core « transaction explicite + retry » : `CreateExecutionStrategy().ExecuteAsync(...)`, `await using` de la transaction sans `try/catch`/rollback manuel (le `Dispose` s'en charge), `RetryLimitExceededException` quand les tentatives sont épuisées. (3) Le seul ajout non fourni par EF : `ChangeTracker.Clear()` en tête du délégué si celui-ci relit des entités qu'une tentative annulée a modifiées. (4) Jamais d'effet de bord hors base (mail, appel externe) dans un délégué rejouable : passer par une outbox traitée par Hangfire.

### Docker « présent mais malade » : c'est containerd, pas dockerd
**Contexte:** Après un redémarrage du conteneur, `dockerd &` semblait lancé mais toute la suite d'intégration échouait sur « Container runtime 'docker' was found but appears to be unhealthy ». La leçon existante disait de relancer `dockerd`, ce que j'avais fait.
**Cause:** Le journal de `dockerd` le disait explicitement : `failed to start containerd: timeout waiting for containerd to start`. Un `containerd` **orphelin** du précédent cycle de vie tenait encore sa socket ; `dockerd` refusait donc de démarrer et ressortait aussitôt, laissant un binaire `docker` fonctionnel mais aucun démon derrière.
**Leçon:** Quand `docker info` échoue alors qu'on vient de lancer `dockerd`, **lire `/tmp/dockerd.log`** avant de relancer une seconde fois à l'identique. Le remède est de tuer le `containerd` orphelin et de nettoyer sa socket (`pkill -x containerd ; rm -f /run/containerd/containerd.sock /var/run/docker.pid`) avant de relancer `dockerd`. Corollaire général : un démon qui « ne démarre pas » a presque toujours écrit pourquoi ; relancer sans lire la trace transforme une panne d'une minute en une demi-heure.

### Une montée de version de PostgreSQL bute sur les volumes Docker des runs précédents
**Contexte:** Après la fusion d'Aspire 13.5.4 (qui passe l'image de `postgres:17.6` à `postgres:18.3`), `dotnet test` restait bloqué quarante minutes au lieu d'en prendre deux. Aucun message d'erreur : la suite ne remontait rien, le processus paraissait simplement lent.
**Cause:** Le conteneur PostgreSQL démarrait puis sortait immédiatement en code 1 (`docker ps -a` le montrait en `Exited (1)`), et les tests attendaient un service qui ne viendrait jamais. Son journal était explicite : l'image 18.x refuse de démarrer sur un répertoire de données créé par la 17.x, ce qui exigerait un `pg_upgrade`. Les volumes `houseflow.apphost-*-postgres-data` des exécutions précédentes survivent aux runs.
**Leçon:** Quand `dotnet test` **traîne** au lieu d'échouer, la piste n'est pas le code mais une dépendance qui ne démarre pas : `docker ps -a` puis `docker logs <conteneur>` en trente secondes. Après toute montée de version majeure de l'image PostgreSQL, supprimer les volumes de données : `docker volume ls --format '{{.Name}}' | grep '^houseflow.apphost-' | xargs -r docker volume rm`. La CI n'est pas concernée, ses runners partent d'un disque vierge — c'est un piège purement local, donc invisible jusqu'à ce qu'il coûte une demi-heure.

### Un process détaché doit rendre le stdout de l'appelant (sinon « Claude stuck »)
**Contexte:** Les sessions restaient régulièrement bloquées sur `verify-e2e.sh | tail` : la suite passait en ~3 min (marqueur écrit), mais la commande ne rendait jamais la main.
**Cause:** `dev-api.sh`/`dev-web.sh` lançaient `setsid bash -c "dotnet run ... > /tmp/api.log" < /dev/null &` — la redirection était *dans* le `bash -c`, donc le shell enveloppe gardait le stdout de l'appelant. Tant que le serveur vivait, le pipe restait ouvert et `tail` attendait un EOF qui ne venait jamais.
**Leçon:** Tout process lancé en arrière-plan par un script redirige stdin/stdout/stderr **à l'extérieur** de l'enveloppe (`setsid bash -c "..." > log 2>&1 < /dev/null &`). Si une commande « bloque » alors que son travail est fini, vérifier `ls -l /proc/<pid>/fd/1` des process restants : un `pipe:[…]` hérité est le coupable.
