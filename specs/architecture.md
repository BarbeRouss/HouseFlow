# House Flow - Architecture Technique

## Stack

| Couche | Technologie |
|--------|-------------|
| **Frontend** | Blazor WebAssembly (.NET 10, C#) |
| **UI** | Tailwind CSS + BlazorBlueprint (icônes Lucide) |
| **Backend** | ASP.NET Core 10 Web API |
| **ORM** | Entity Framework Core |
| **Base de données** | PostgreSQL |
| **Auth** | JWT (refresh token en cookie HTTP-only) |
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

## Déploiement

### Local (développement)
```bash
dotnet run --project src/HouseFlow.AppHost
```
Lance : API (.NET) + Frontend (Blazor WASM) + PostgreSQL (Docker), orchestrés par .NET Aspire.

### Azure — un environnement complet par instance

**Infrastructure :** Terraform (`infrastructure/terraform/`), une racine unique `environment/`
instanciée par un `name` et un jeu de variables dans `instances/`. Un environnement possède tout
ce dont il dépend — son resource group, son VNet, son serveur PostgreSQL, son Container Apps
Environment, son identité :

```
rg-houseflow-prod      vnet · psql-houseflow-prod · cae · ca-api-prod · swa-prod · id-prod
rg-houseflow-<nom>     la même chose, avec un tag ttl        (preprod, pr-<n>, essais nommés)
rg-houseflow-shared    kv-houseflow · id-houseflow-cert · states · conteneur db-dumps
```

La production n'est pas un cas particulier du code : c'est l'instance dont l'échéance est vide et
le resource group verrouillé. C'est ce qui rend un changement d'infrastructure éprouvable — une
montée de version majeure de PostgreSQL, un changement de SKU ou de subnet s'applique sur un
environnement jetable, jamais sur celui qui porte la production faute d'autre cible.

La production et les environnements jetables vivent dans **deux souscriptions distinctes**, du
même tenant. Le seul lien est le certificat wildcard, lu depuis le Key Vault de production par
l'identité de certificat de la souscription jetable.

**Authentification CI/CD :**
- GitHub Actions → Azure : Workload Identity Federation (OIDC), une app registration par
  environnement GitHub (`prod`, `preprod`, `preview`)
- Azure → GHCR : PAT classique `read:packages`

**Workflows :**

```
merge main ──► build ──► apply-shared ──► certificat        pipeline.yml
                     ──► plan-prod       plan publié et archivé
                     ──► approbation     lecture du plan
                     ──► apply-prod      applique CE plan, pas un nouveau

PR ouverte ──► environnement COMPLET pr-<n>                 pr-preview.yml
PR fermée  ──► destroy · filet : tag ttl + reaper

à la demande ► create / destroy d'une instance nommée       environment.yml
horaire ─────► suppression des resource groups expirés      reaper.yml
```

L'approbation arrive **après** le plan : un environnement de PR est toujours créé depuis zéro,
donc il prouve que le code produit une infrastructure qui fonctionne, jamais que ce même code
appliqué à l'état existant de la production est inoffensif. `preprod` n'est pas une étape du
pipeline — c'est une instance lancée à la main.

**Protections :** rôle Azure custom `HouseFlow Deployer` plutôt que Contributor et sans droit
d'attribution de rôle, Azure Policy (allowlist de types + SKU PostgreSQL restreints), lock
`CanNotDelete` sur le resource group et la base de production, garde-fou `tf-plan-guard.sh`
contre la destruction des ressources critiques (CAE, PostgreSQL, Key Vault, VNet, identités).

Détail complet (souscriptions, RBAC, instances, flux de déploiement, reaper, bootstrap) :
[`specs/infrastructure.md`](infrastructure.md).

### DNS

**Domaine :** `houseflow.cloud`, chez OVH, piloté par Terraform (provider `ovh/ovh`). Chaque
environnement pose ses propres enregistrements, via le module `modules/ovh-dns-zone` : il n'y a
plus de stack DNS centrale qui devrait connaître à l'avance tous les hôtes.

| Enregistrement | Cible |
|---|---|
| `www`, `api` | prod |
| `preprod`, `api-preprod` | preprod |
| `<nom>`, `api-<nom>` | l'instance qui porte ce nom, `pr-<n>` compris |

Un seul label sous `houseflow.cloud` (`api-preprod`, pas `api.preprod`) : le certificat wildcard
`*.houseflow.cloud` ne couvre qu'un niveau. La zone est le seul point de contention entre
environnements — tous les applies partagent le groupe de concurrence `ovh-dns-zone`, qui les
sérialise. Les credentials OVH ne sortent jamais de la CI. Le module ne gère jamais
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
(ressource `azapi`, identité partagée `id-houseflow-cert`) plutôt que de l'importer localement :
une nouvelle version dans Key Vault est reprise automatiquement, sans redéploiement. Seuls les
hôtes d'API s'y lient — les Static Web Apps émettent le leur par délégation CNAME, et ne
consomment donc pas le quota Let's Encrypt.

Ce que le wildcard ne remplace pas : le TXT `asuid.<hôte>` reste exigé par Azure pour **chaque**
hostname custom (preuve de propriété, indépendante du certificat).

Détail (format PFX, idempotence, serveur ACME de staging) :
[`specs/infrastructure.md`](infrastructure.md).

---

## Coûts estimés (MVP)

La production est la seule dépense permanente : les environnements jetables ne vivent que douze
heures et le reaper les ramasse.

| Service | Coût |
|---------|------|
| Static Web App (SKU Free), VNet, identités, Key Vault | 0€ |
| Azure Container Apps (Consumption) — un réplica d'API maintenu en prod | négligeable |
| Azure PostgreSQL prod (B1ms, 32 Go) | ~15€/mois |
| Log Analytics prod (au Go ingéré) | ~1€/mois |
| **Total permanent** | **~16€/mois** |

Un environnement jetable coûte son propre serveur PostgreSQL au prorata de sa durée de vie — le
poste qui a remplacé la mutualisation, et ce que le TTL de douze heures borne. Le plafond de
previews simultanées (`MAX_PR_ENVS`) est fixé par le quota de vCores de la souscription jetable,
pas par le coût.

---
