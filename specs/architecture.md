# House Flow - Architecture Technique

## Stack

| Couche | Technologie |
|--------|-------------|
| **Frontend** | Blazor WebAssembly (.NET 10, C#) |
| **UI** | Tailwind CSS + BlazorBlueprint (icônes Lucide) |
| **Backend** | ASP.NET Core 10 Web API |
| **ORM** | Entity Framework Core |
| **Base de données** | PostgreSQL |
| **Auth** | JWT (refresh token en cookie HTTP-only) ; OAuth 2.1 embarqué (OpenIddict) pour les clients MCP |
| **Orchestration** | .NET Aspire |
| **Conteneurs** | Docker |
| **Déploiement** | Azure Container Apps (API) + Static Web App (frontend), Terraform |

---

## Structure du projet

```
/src
  /HouseFlow.AppHost        # .NET Aspire orchestrateur
  /HouseFlow.Core           # Entités et interfaces
  /HouseFlow.Application    # Services métier, DTOs, contrats
  /HouseFlow.Infrastructure # EF Core, migrations, jobs
  /HouseFlow.API            # Contrôleurs REST
  /HouseFlow.Web            # Frontend Blazor WebAssembly (.razor)
  /HouseFlow.WebHost        # Hôte ASP.NET servant le WASM publié
```

---

## Frontend : design system

La référence visuelle est [`specs/ux/`](ux/README.md) (handoff hi-fi : tokens, composants,
15 pages et 7 modales en 3 largeurs × 2 thèmes). Son implémentation :

- **Tokens** : variables CSS clair (`:root`) / sombre (`.dark`) dans
  `src/HouseFlow.Web/Styles/app.input.css`, exposées à Tailwind v4 par `@theme inline` (couleurs,
  rayons, ombres, largeurs de contenu), plus des briques `hf-*` (boutons, champs, cartes,
  pastilles…). Aucune couleur brute dans `Features/` : les teintes par type d'appareil et par
  couleur de maison sont dérivées en CSS (`color-mix`) d'un attribut `data-type` /
  `data-house-color`.
- **Polices** : Bricolage Grotesque (titres) et Instrument Sans (texte) **auto-hébergées** en
  woff2 (`wwwroot/fonts`, licence OFL). Aucune requête vers un CDN de polices : ce serait
  transmettre l'IP du visiteur à un tiers.
- **Thème** : Clair / Sombre / Système, classe `.dark` sur `<html>`, posée par `index.html` avant
  le premier rendu (pas de flash) puis par `ThemeService`.
- **Icône de l'app par statut** : logo du header et favicon (`wwwroot/icons/{icon,favicon}-{ok,due,late,none}.svg`)
  suivent le statut global dérivé des compteurs de `GET /dashboard` (`AppIconService`) ;
  icône `ok` fixe sur les pages publiques, `none` pendant l'onboarding et avant la réponse.
  Manifeste PWA `wwwroot/manifest.webmanifest` (PNG 192/512).

Détail des composants et des classes : [`PROJECT_KNOWLEDGE.md`](../PROJECT_KNOWLEDGE.md)
§ *Design System* et *Frontend Architecture*.

---

## Schéma de données

Le schéma courant — entités, champs et relations — est décrit dans
[`PROJECT_KNOWLEDGE.md`](../PROJECT_KNOWLEDGE.md) § *Database Schema*, tenu à jour à chaque
migration. Ne pas le recopier ici : une copie diverge dès la migration suivante.

---

## API

Le contrat est [`openapi.yaml`](openapi.yaml), et il fait autorité : les DTOs et les bases de
contrôleurs en sont générés par NSwag (`nswag-dtos.json`, `nswag-controllers.json`). Toute liste
d'endpoints recopiée dans un document deviendrait fausse en silence — c'était le cas de celle qui
figurait ici, restée sur `/api/...` alors que l'API est versionnée `/api/v1/...`.

---

## Modèle d'autorisation (RBAC)

Quatre rôles par maison. Chaque endpoint vérifie le rôle du membre avant d'autoriser
l'action ; un utilisateur non-membre reçoit 403. Les réponses API masquent les champs
coûts et prestataire pour les locataires.

| Action | Owner | Collaborator RW | Collaborator RO | Tenant |
|--------|:---:|:---:|:---:|:---:|
| Voir maison / appareils | ✅ | ✅ | ✅ | ✅ |
| Voir coûts / prestataires | ✅ | ✅ | ✅ | ❌ |
| Logger un entretien | ✅ | ✅ | ❌ | ⚙️ `canLogMaintenance` |
| Créer type d'entretien | ✅ | ✅ | ❌ | ❌ |
| CRUD appareil | ✅ | ✅ | ❌ | ❌ |
| Modifier / supprimer maison | ✅ | ❌ | ❌ | ❌ |
| Inviter collaborateur | ✅ | ❌ | ❌ | ❌ |
| Inviter locataire | ✅ | ✅ | ❌ | ❌ |
| Gérer permissions membres | ✅ | ❌ | ❌ | ❌ |
| Retirer un membre | ✅ | ❌ | ❌ | ❌ |

`canLogMaintenance` est un drapeau par locataire, activé par défaut, modifiable par le
seul propriétaire.

Implémentation : `HouseMemberService.RequireRoleAsync` (`src/HouseFlow.Application/Services/`),
masquage des coûts dans `MaintenanceService`, couverture dans
`tests/HouseFlow.IntegrationTests/Collaboration/RbacPermissionTests.cs`.

## Authentification OAuth 2.1 pour les clients MCP (Authorization Server embarqué)

L'API est aussi un **Authorization Server OAuth 2.1** (OpenIddict, stores EF Core dans `HouseFlowDbContext`),
pour que des clients MCP grand public (Claude) accèdent aux données d'un utilisateur **sans mot de passe ni
token longue durée**. Le serveur MCP lui-même (Resource Server, `/mcp`) est décrit dans la section suivante.

| Élément | Choix |
|---|---|
| Flux | Authorization code + **PKCE S256 obligatoire** ; refresh token avec rotation stricte (réutilisation refusée). Aucun autre grant. |
| Clients | Enregistrement dynamique `POST /connect/register` (RFC 7591), clients **publics** (pas de secret), redirect URIs `https://` ou loopback uniquement ; ou **Client ID Metadata Document** : `client_id` = URL https dont HouseFlow télécharge le document (voir ci-dessous). |
| Endpoints | `/connect/authorize`, `/connect/token`, `/connect/userinfo`, `/connect/revocation` ; métadonnées RFC 8414 `/.well-known/oauth-authorization-server` (et `/.well-known/openid-configuration`). |
| Scopes | `houses:read`, `houses:write` (+ `offline_access`, mécanisme interne des refresh tokens). |
| Durées | access token 15 min (chiffré, opaque pour le client), code 5 min, refresh token 30 jours, cookie de session OAuth 10 min. |
| Audience | `resource` RFC 8707 = URL du serveur MCP (`OAuth:Resources`, sinon `{host}/mcp`) ; un token OAuth n'est jamais accepté par l'API REST, et réciproquement. |
| Identification sur `/connect/authorize` | cookie de session dédié `oauthSession` (JWT HS256 à audience propre, `Path=/connect`, 10 min, effacé au login/logout/suppression) posé par `POST /api/v1/oauth/session` depuis le front connecté — le cookie de refresh existant (`/api/v1/auth`) n'est pas élargi. Un claim à usage unique « consentement à l'instant » est le seul moyen de sauter l'écran. |
| Consentement | écran Blazor `/oauth/consent` (nom du client, **hôte de redirection de la requête**, scopes cochables, « Autoriser » armé après un court délai) ; la décision est une autorisation permanente OpenIddict **liée à l'hôte consenti**, redemandée si le client veut un scope non accordé, un autre hôte, ou livre le code à un listener loopback (client natif, RFC 8252 §8.6) ; listée et révocable dans « Réglages › Applications connectées ». |
| Clés | signature HS512 et chiffrement A256KW **dérivées (HKDF) de `Jwt:Key`** — identiques sur toutes les répliques et après redémarrage, aucun secret supplémentaire ; une clé RSA éphémère ne sert qu'à satisfaire le contrôle de démarrage d'OpenIddict (aucun id_token n'est émis, un test d'intégration le prouve). Tourner `JWT__KEY` invalide aussi tous les jetons OAuth. |

Ce que OAuth ne couvre pas : une fois `houses:write` accordé, le client agit avec l'autorité de l'utilisateur
(injection de prompt côté LLM). Les scopes, la durée courte, la révocation et l'audit bornent le rayon d'action ;
`houses:write` reste refusable sur l'écran de consentement sans refuser la lecture.

Configuration (section `OAuth`, tout optionnel) : `WebBaseUrl` (défaut : première origine de `CORS__ORIGINS`),
`Issuer` et `Resources` (défaut : hôte de la requête — à fixer en production), durées. Implémentation :
`src/HouseFlow.API/OAuth/`, `Controllers/OAuthConnectController.cs`, `Controllers/OAuthAuthorizationsController.cs`,
`src/HouseFlow.Application/OAuth/`, écrans `src/HouseFlow.Web/Features/OAuth/`, tests `tests/HouseFlow.IntegrationTests/OAuth/`.
### Client ID Metadata Documents (CIMD, issue #308)

Un `client_id` qui est une URL https (`https://claude.ai/.well-known/oauth-client-metadata`) rend l'**origine du
client vérifiable** : l'écran de consentement affiche l'hôte du `client_id` comme identité vérifiée au lieu de
l'avertissement anti-hameçonnage. Décisions durables :

- **Règle « même hôte »** : toute `redirect_uri` https du document doit être sur **exactement l'hôte du `client_id`**
  (port par défaut). Pas de liste des suffixes publics : ni dépendance ni liste à tenir à jour, ni cas limites
  (`github.io`, `azurewebsites.net`). Un client qui sépare ses hôtes garde le DCR. Les `redirect_uris` loopback
  restent admises (clients natifs) mais **sans badge** : tout programme local peut présenter un `client_id` public.
- **Grant types** : un document décrit le client, il ne s'adresse pas à HouseFlow. Les grant types que HouseFlow ne gère
  pas (`jwt-bearer` chez Claude, `device_code` chez VS Code) sont **ignorés** ; l'application ne reçoit que
  `authorization_code` et, s'il est déclaré, `refresh_token`. Le document n'est refusé que si `authorization_code`
  manque. Le DCR garde sa règle stricte (tout autre grant type est refusé).
- **Politique SSRF sortante** (`ClientMetadataDocumentFetcher`, seul endroit où HouseFlow télécharge une URL fournie
  par un appelant anonyme) : https, port 443, `ConnectCallback` qui résout le DNS, **refuse si une seule adresse n'est
  pas publique** (loopback, RFC 1918, link-local dont l'IMDS Azure, CGNAT, multicast, IPv6 locale, IPv4-mapped déballées,
  wireserver Azure `168.63.129.16`) puis se connecte à l'adresse vérifiée (le TLS valide toujours le nom d'hôte : pas de
  DNS rebinding) ; aucune redirection, aucun proxy ni cookie, pas de décompression, `200` + `application/json`, 16 Ko
  lus sans se fier à `Content-Length`, 5 s ; limite par IP appelante (10/min) et par URL (6/min), échec mémorisé 1 min.
  Les logs portent l'hôte et la raison, jamais le document.
- **Cache = la ligne `OpenIddictApplications`** (propriétés `cimd`, `cimd_fetched_at`, `cimd_expires_at`, aucune
  migration) : TTL = `Cache-Control: max-age` borné à [5 min, 1 h], 1 h par défaut, partagé par toutes les répliques.
  Rafraîchissement **uniquement sur `/connect/authorize`** ; en cas d'échec (réseau, statut, document invalide)
  l'autorisation est refusée, sans copie périmée. `/connect/token` ne télécharge jamais : un jeton ne dépend pas de la
  disponibilité d'un tiers. Un client CIMD est une application **partagée** (clé = URL) ; consentements et
  autorisations restent par utilisateur. Un document modifié met l'application à jour : une `redirect_uri` retirée est
  refusée dès l'`/authorize` suivant, les jetons déjà émis vivent jusqu'à leur révocation normale.
- Le handler OpenIddict `ClientMetadataDocumentHandler` s'exécute **avant** `Authentication.ValidateAuthentication` (qui exécute `ValidateClientId`) ; un refus est
  `invalid_client` affiché au navigateur, jamais une redirection vers une `redirect_uri` non validée.

## Serveur MCP (Resource Server, `/mcp`)

Le SDK officiel `ModelContextProtocol.AspNetCore` expose, dans `HouseFlow.API`, un serveur MCP en
**Streamable HTTP** (pas de stdio), sans état (`Stateless`) : chaque appel est authentifié.

| Élément | Choix |
|---|---|
| URL | `https://<api>/mcp` — à saisir comme « connecteur personnalisé » dans Claude (l'OAuth se découvre seul). |
| Authentification | tokens OAuth du serveur d'autorisation embarqué uniquement (schéma OpenIddict Validation, politique `McpRead`) ; le JWT de l'API REST et les clés d'API sont refusés (401), et inversement. |
| Découverte | `401` + `WWW-Authenticate: Bearer resource_metadata="…/.well-known/oauth-protected-resource"` ; métadonnées RFC 9728 (ressource, serveur d'autorisation, scopes, `header`), aussi sous `…/oauth-protected-resource/mcp`. |
| Contrôles | signature/expiration/révocation (OpenIddict), **audience** = ressource MCP, scope `houses:read` (403 sinon) ; chaque tool revérifie le scope qu'il exige (`houses:read` pour la lecture, `houses:write` pour l'écriture). |
| Tools de lecture | `list_houses`, `get_house`, `list_devices`, `get_device`, `list_interventions`, `list_upcoming_tasks` — ils appellent les services Application existants avec l'identité du token : **même contrôle d'accès par foyer que l'API REST**, un foyer inaccessible est indiscernable d'un foyer inexistant. |
| Tool d'écriture | `log_intervention` (scope `houses:write`, non destructif, non idempotent : crée une entrée à chaque appel) — enregistre une intervention réalisée via `MaintenanceService.LogMaintenanceAsync`, donc mêmes règles que l'API REST (accès au foyer, date non future, masquage des coûts) ; date `yyyy-MM-dd`, coût, fournisseur et notes validés comme côté REST. |
| Pagination | `limit` (1-50, défaut 25) / `offset` ; réponses `{ items, total, offset, hasMore }`. |
| Audit | une ligne `AuditLogs` par appel (`EntityType=McpTool`, `EntityId`=tool, `Action=McpCall`, utilisateur, horodatage, IP, user agent) — ni token, ni donnée renvoyée. |

Implémentation : `src/HouseFlow.API/Mcp/` ; tests `tests/HouseFlow.IntegrationTests/Mcp/`. En production, Terraform (`infrastructure/terraform/environment/apps.tf`, local `oauth_env`) fixe `OAuth__Issuer=https://api.houseflow.cloud` et `OAuth__Resources__0=https://api.houseflow.cloud/mcp` sur l'API de la seule instance permanente (valeurs publiques, hors Key Vault, 0 $). Les environnements de PR ne les portent pas : leurs métadonnées annoncent leur propre hôte (`api-pr-<n>`).

---

## Déploiement

### Local (développement)
```bash
dotnet run --project src/HouseFlow.AppHost
```
Lance : API (.NET) + Frontend (Blazor WASM) + PostgreSQL (Docker), orchestrés par .NET Aspire.

### Azure — un environnement complet par instance

**Infrastructure :** Terraform (`infrastructure/terraform/`), une racine `environment/`
instanciée par un `name` et un jeu de variables dans `instances/` — complétée, pour le DNS, par
les racines `dns/` et `custom-domains/` (voir « DNS » ci-dessous). Un environnement possède tout
ce dont il dépend — son resource group, son VNet, son serveur PostgreSQL, son Container Apps
Environment, son identité :

```
rg-houseflow-prod      vnet · psql-houseflow-prod · cae · ca-api-prod · swa-prod · id-prod
rg-houseflow-pr-<n>    la même chose, avec un tag ttl        (un par pull request)
rg-houseflow-shared-*  kv-houseflow · id-houseflow-cert-* · id-houseflow-dumps-writer · states · conteneur db-dumps
                        (-prod / -ephemeral selon la souscription)
```

Deux instances seulement, et c'est délibéré : la production, permanente, et l'environnement d'une
pull request.

**Données d'un environnement de PR :** une copie **pseudonymisée** de la prod de la nuit. Le job
`dbtools dump` de la production pseudonymise une copie de la base (tous les comptes sauf une
allow-list), la vérifie, et ne publie dans `db-dumps` qu'un dump sans donnée personnelle ; le job
`dbtools restore` de la PR le restaure à la création de l'environnement, puis l'API redémarre et
applique les migrations de la branche sur ces données. Détail : `specs/infrastructure.md`,
`dbtools/README.md`.

La production n'est pas un cas particulier du code : c'est l'instance dont l'échéance est vide, ce
dont découlent le verrou du resource group et le réplica d'API maintenu. Ces deux-là ne sont pas
des réglages mais des conséquences, et pas par souci d'économie : en faire des variables rendrait
représentable l'environnement à la fois éphémère et verrouillé, c'est-à-dire un resource group
promis au reaper qu'il ne peut pas détruire. Un état qu'on ne peut pas écrire est un état qu'on ne
peut pas atteindre par erreur.

C'est aussi ce qui rend un changement d'infrastructure éprouvable — une montée de version majeure
de PostgreSQL, un changement de SKU ou de subnet s'applique sur un environnement jetable, jamais
sur celui qui porte la production faute d'autre cible.

La production et les environnements jetables vivent dans **deux souscriptions distinctes**, du
même tenant. Le seul lien est le certificat wildcard, lu depuis le Key Vault de production par
l'identité de certificat de la souscription jetable.

**Authentification CI/CD :**
- GitHub Actions → Azure : Workload Identity Federation (OIDC), une app registration par
  environnement GitHub qui accède à Azure (`prod`, `preview`)
- Azure → GHCR : PAT classique `read:packages`

**Workflows :**

```
merge main ──► build ──► apply-shared ──► certificat        pipeline.yml
                     ──► plan-prod       plan publié et archivé
                     ──► approbation     lecture du plan
                     ──► apply-prod      applique CE plan, pas un nouveau

PR ouverte ──► environnement COMPLET pr-<n>                 pr-preview.yml
PR fermée  ──► destroy · filet : tag ttl + reaper

horaire ─────► suppression des resource groups expirés      reaper.yml
```

L'approbation arrive **après** le plan : un environnement de PR est toujours créé depuis zéro,
donc il prouve que le code produit une infrastructure qui fonctionne, jamais que ce même code
appliqué à l'état existant de la production est inoffensif.

Il n'existe pas d'environnement de validation intermédiaire, et il n'en manque pas : l'environnement
d'une PR est complet — son réseau, son serveur PostgreSQL, son Container Apps Environment — et il
est produit par le même `terraform apply` que celui qui touchera la production au merge. Un
changement d'infrastructure est donc déjà éprouvé dans la PR qui l'introduit ; un environnement
permanent de plus ne prouverait rien de nouveau et se facturerait en continu.

**Protections :** rôle Azure custom `HouseFlow Deployer` plutôt que Contributor et sans droit
d'attribution de rôle, Azure Policy (allowlist de types + SKU PostgreSQL restreints), lock
`CanNotDelete` sur le resource group et la base de production, garde-fou `tf-plan-guard.sh`
contre la destruction des ressources critiques (CAE, PostgreSQL, Key Vault, VNet, identités).

Détail complet (souscriptions, RBAC, instances, flux de déploiement, reaper, bootstrap) :
[`specs/infrastructure.md`](infrastructure.md).

### Previews de PR sur le homelab

Décision de Julien du 2026-11-12 (issue #348) : chaque PR de ce dépôt est validée sur une **preview
docker compose** de la LXC `houseflow-preview` (arm64, homelab), pas sur Azure ; Azure ne garde que la
production et la preprod. Décision et compromis : [`docs/design/02-previews-homelab.md`](../docs/design/02-previews-homelab.md) ;
exploitation : [`deploy/preview/README.md`](../deploy/preview/README.md).

```
client ─HTTPS─▶ Traefik du homelab ─HTTP─▶ Traefik de la LXC ─▶ web   <n>.pr.houseflow.cloud
                TLS *.pr.houseflow.cloud    provider Docker       api   api-<n>.pr.houseflow.cloud
                geoblock-be                 réseau « previews »   postgres (réseau du projet seulement)

PR ouverte/poussée ─▶ preview.yml : resolve ─▶ build (hébergé arm64 → GHCR) ─▶ deploy (runner LXC) ─▶ report
PR fermée          ─▶ preview.yml : cleanup-preview (down) + cleanup-registry (label, versions GHCR)
horaire            ─▶ preview-reaper.yml : TTL glissant 7 jours, PR fermées, purge GHCR
```

- **Build hors de la LXC** : les trois images `ghcr.io/barberouss/houseflow-preview-{api,web,dbtools}:pr-<n>`
  sont construites en `linux/arm64` natif sur `ubuntu-24.04-arm` ; la LXC ne fait que les tirer
  (`preview.sh up`), jamais de `docker build`.
- **Sécurité du runner** (dépôt public, runner persistant) : déclencheurs `pull_request` et
  `workflow_dispatch` seulement ; chaque job du runner conditionné à
  `head.repo.full_name == github.repository` et hors Dependabot ; droits minimaux, aucun secret du dépôt ;
  `scripts/ci/check-self-hosted-guard.sh` le vérifie en CI. Ces garde-fous protègent contre l'erreur d'un
  contributeur honnête (une PR de fork exécute le workflow de la PR) : la barrière contre un fork hostile est le
  réglage GitHub « Require approval for all outside collaborators ».
- **Plafond** de 5 previews (`MAX_PREVIEWS`), vérifié avant tout pull ; **TTL** glissant de 7 jours depuis le
  dernier déploiement (`PREVIEW_TTL_HOURS`) ; suppression à la fermeture de la PR.
- **Données** : le dump pseudonymisé de la prod, lu en OIDC (`id-houseflow-dumps-reader`, environnement
  GitHub `preview-homelab`, sans secret), restauré une fois par preview ; PostgreSQL sans TLS sur le réseau
  interne du projet (la preprod et la prod gardent TLS + Entra).
- **Tests** inchangés : tous sur les runners hébergés de `pr.yml`. Pas d'E2E contre la preview (un runner
  hébergé ne passerait pas `geoblock-be`) : la validation de la preview est celle de product.

### DNS

**Domaine :** `houseflow.cloud`, chez OVH, piloté par Terraform (provider `ovh/ovh`). Chaque
environnement pose ses propres enregistrements, via le module `modules/ovh-dns-zone` : il n'y a
plus de stack DNS centrale qui devrait connaître à l'avance tous les hôtes. Les enregistrements
sont calculés par `environment`, écrits par la racine `dns`, puis liés côté Azure par la racine
`custom-domains` — trois states par environnement, pour que le verrou de la zone ne couvre que
les écritures OVH.

| Enregistrement | Cible |
|---|---|
| `www`, `api` | prod |
| `pr-<n>`, `api-pr-<n>` | l'environnement de la pull request `<n>` |

Un seul label sous `houseflow.cloud` (`api-pr-42`, pas `api.pr-42`) : le certificat wildcard
`*.houseflow.cloud` ne couvre qu'un niveau. La zone est le seul point de contention entre
environnements — seule la racine `dns` y écrit, sous le groupe de concurrence `ovh-dns-zone`, qui
sérialise ces écritures. Les credentials OVH ne sortent jamais de la CI. Le module ne gère jamais
l'enregistrement racine (`""`) de la zone.

**Redirection apex → www (hors Terraform) :** `houseflow.cloud` (apex nu) redirige vers `www.houseflow.cloud` via la redirection de domaine OVH, une fonctionnalité distincte de la zone DNS classique (endpoint `/domain/zone/{zone}/redirection`, pas `/record`). C'est une configuration **statique**, faite manuellement dans l'espace client OVH — le provider Terraform `ovh/ovh` ne l'expose pas, elle ne doit jamais être recréée ou modifiée par ce module.

**Création/rotation des credentials API OVH :**
1. Générer un token sur https://api.ovh.com/createToken/, avec 4 droits sur le chemin `/domain/zone/houseflow.cloud/*` : `GET`, `POST`, `PUT`, `DELETE`
2. Valider le Consumer Key via l'URL de confirmation renvoyée (connexion + 2FA)
3. Stocker `OVH_APPLICATION_SECRET` et `OVH_CONSUMER_KEY` en secrets GitHub du repo, `OVH_APPLICATION_KEY` en variable de repo
4. Pour une rotation : générer un nouveau token avec le même scope, mettre à jour les 3 valeurs, révoquer l'ancien token dans l'espace client OVH

**Ancien domaine décommissionné :** la prod servait auparavant depuis `houseflow.rouss.be` / `api.houseflow.rouss.be` (sous-domaine de `rouss.be`, hors zone gérée par Terraform). Ces enregistrements DNS restent temporairement en place chez OVH, gérés manuellement, le temps de vérifier `houseflow.cloud` en conditions réelles — à supprimer manuellement une fois cette vérification faite.

### Certificat TLS

Certificat wildcard `*.houseflow.cloud` (+ SAN `houseflow.cloud`), Let's Encrypt, validation
DNS-01 contre la zone OVH (`lego`, compte ACME persisté dans Key Vault). Émis par le job
`certificate` de `pipeline.yml` (après l'infra partagée, sur cron mensuel, ou à la demande via
`force_certificate`) et stocké dans `kv-houseflow` (`wildcard-houseflow-cloud`).

Chaque Container Apps Environment référence ce certificat **directement dans Key Vault**
(ressource `azapi`, identité partagée `id-houseflow-cert-prod` ou `-ephemeral` selon la
souscription) plutôt que de l'importer localement :
une nouvelle version dans Key Vault est reprise automatiquement, sans redéploiement. Seuls les
hôtes d'API s'y lient — les Static Web Apps émettent le leur par délégation CNAME, et ne
consomment donc pas le quota Let's Encrypt.

Ce que le wildcard ne remplace pas : le TXT `asuid.<hôte>` reste exigé par Azure pour **chaque**
hostname custom (preuve de propriété, indépendante du certificat).

Détail (format PFX, idempotence, serveur ACME de staging) :
[`specs/infrastructure.md`](infrastructure.md).

---

## Coûts estimés (MVP)

La production est la seule dépense permanente : les environnements de PR ne vivent que quatre
heures et le reaper les ramasse.

| Service | Coût |
|---------|------|
| Static Web App (SKU Free), VNet, identités, Key Vault | 0€ |
| Azure Container Apps (Consumption) — un réplica d'API maintenu en prod | négligeable |
| Azure PostgreSQL prod (B1ms, 32 Go) | ~15€/mois |
| Log Analytics prod (au Go ingéré) | ~1€/mois |
| **Total permanent** | **~16€/mois** |

Un environnement jetable coûte son propre serveur PostgreSQL au prorata de sa durée de vie — le
poste qui a remplacé la mutualisation, et ce que le TTL de quatre heures borne — un oubli ne
facture jamais plus d'une demi-journée, au prix d'une PR dormante à réveiller. Le plafond de
previews simultanées (`MAX_PR_ENVS`) est fixé par le quota de vCores de la souscription jetable,
pas par le coût.

---
