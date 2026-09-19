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
| **Déploiement** | Azure Container Apps (Terraform) |

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

### Production & Preprod — Azure Container Apps

**Infrastructure :** Terraform (`infrastructure/terraform/`), organisée en 4 resource groups
isolés par frontière — chaque environnement (`preprod`, `preview`, `prod`) a son propre VNet,
son Container Apps Environment (CAE), ses apps et son identité managée ; `shared` ne porte que
de la donnée et des secrets, appliqué par l'identité prod derrière la gate d'approbation :

```
rg-houseflow-shared    psql-houseflow (PostgreSQL) · kv-houseflow · tfstate · identités managées
rg-houseflow-preprod   cae-houseflow-preprod  ──peering──►  rg-houseflow-shared
rg-houseflow-preview   cae-houseflow-preview  ──peering──►  rg-houseflow-shared
rg-houseflow-prod      cae-houseflow-prod     ──peering──►  rg-houseflow-shared
```

`psql-houseflow` est un serveur PostgreSQL unique et partagé ; la frontière entre environnements
est portée par les rôles PostgreSQL (une base et des droits distincts par environnement), pas par
des serveurs séparés. L'administration SQL (création de rôles, de bases) passe par des Container
Apps Jobs (`job-dbtools-roles`, `job-dbtools-init`) exécutés dans le VNet peeré — le runner
GitHub n'a aucun accès réseau direct au serveur.

**Authentification CI/CD :**
- GitHub Actions → Azure : Workload Identity Federation (OIDC), une app registration par
  environnement GitHub (`preprod`, `preview`, `prod`)
- Azure → GHCR : PAT fine-grained `read:packages`

**Pipeline :** un unique workflow, `.github/workflows/pipeline.yml` (push sur `main`,
`workflow_dispatch`, cron pour le renouvellement du certificat), enchaîne infra, base de
données, certificat, DNS et déploiement :

```
build ─┬─ apply-shared ─ env-prod ─ dbtools-roles ─┐
       ├─ env-preprod ──────────────────────────────┤
       ├─ env-preview ──────────────────────────────┤
       └─ certificate ─ dns ──────────────────────────┼─ deploy-preprod ─ approve-prod ─ deploy-prod
```

Une seule approbation humaine par push, portée par un job vide (`approve-infra` ou
`approve-prod` selon ce qui a changé) sur l'environnement GitHub dédié `prod-approval` — jamais
par les jobs qui font le travail. `pr-preview.yml` déploie séparément les previews de PR sur le
CAE `preview`.

**Protections :** rôles Azure custom (`HouseFlow Deployer`, `HouseFlow Shared Tenant`) plutôt
que Contributor, Azure Policy (allowlist de types + SKU PostgreSQL restreints), lock
`CanNotDelete` sur `rg-houseflow-prod`, garde-fous Terraform contre la destruction des ressources
critiques (CAE, PostgreSQL, Key Vault, VNet, identités).

Détail complet (resource groups, RBAC, rôles PostgreSQL, image `dbtools`, stacks Terraform,
graphe complet du pipeline, bootstrap) : [`specs/infrastructure.md`](infrastructure.md).

### DNS

**Domaine :** `houseflow.cloud`, chez OVH, piloté par Terraform (provider `ovh/ovh`) — stack
`dns`, appliquée par l'environnement `prod` après les trois stacks `env-*`.

| Enregistrement | Cible |
|---|---|
| `www`, `api` | prod |
| `preprod`, `api-preprod` | preprod |
| `pr-<n>`, `api-pr-<n>` | preview (créés/détruits par le stack `ephemeral`) |

Un seul label sous `houseflow.cloud` (`api-preprod`, pas `api.preprod`) : le certificat wildcard
`*.houseflow.cloud` ne couvre qu'un niveau. Un plan qui supprime des enregistrements est refusé
sans marqueur explicite (`[dns-allow-destroy]` dans le commit ou `allow_dns_destroy` en input de
dispatch). Exécuté uniquement en CI (job `dns` de `pipeline.yml`) — jamais avec des credentials
OVH en session interactive. Le module ne gère jamais l'enregistrement racine (`""`) de la zone.
Détail (module `ovh-dns-zone`, ordre d'application, data sources sur les CAE) :
[`specs/infrastructure.md`](infrastructure.md).

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
(ressource `azapi`, identité managée de l'environnement) plutôt que de l'importer localement :
une nouvelle version dans Key Vault est reprise automatiquement, sans redéploiement. Les
domaines custom de prod et preprod se lient à ce certificat d'environnement ; les Static Web
Apps des previews gèrent leur propre certificat managé.

Ce que le wildcard ne remplace pas : le TXT `asuid.<hôte>` reste exigé par Azure pour **chaque**
hostname custom (preuve de propriété, indépendante du certificat).

Détail (format PFX, idempotence, serveur ACME de staging) :
[`specs/infrastructure.md`](infrastructure.md).

---

## Coûts estimés (MVP)

| Service | Coût |
|---------|------|
| Azure Container Apps (Consumption), VNet, peering, identités, Key Vault | 0€ / négligeable |
| Azure PostgreSQL (B1ms, 32 Go, partagé par les 3 environnements) | ~15€/mois |
| Log Analytics (3 workspaces, un par environnement, au Go ingéré) | ~2-3€/mois |
| **Total** | **~17-18€/mois** |

---
