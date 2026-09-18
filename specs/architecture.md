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

**Infrastructure :** Terraform (`infrastructure/terraform/`)

```
Resource Group: rg-houseflow
├── Container Apps Environment: cae-houseflow
│   ├── ca-api-prod        (port 8080, /alive health check)
│   ├── ca-frontend-prod   (port 3000)
│   ├── ca-api-preprod
│   ├── ca-frontend-preprod
│   └── ca-api-pr-XX / ca-frontend-pr-XX  (éphémères par PR)
├── PostgreSQL Flexible Server: psql-houseflow (B1ms)
│   ├── houseflow_prod
│   ├── houseflow_preprod
│   └── houseflow_pr_XX   (éphémères par PR)
├── Log Analytics Workspace: law-houseflow
└── Storage Account: sthouseflowtfstate (Terraform state)
```

**Authentification CI/CD :**
- GitHub Actions → Azure : Workload Identity Federation (OIDC, pas de secret)
- Azure → GHCR : PAT fine-grained `read:packages`

**Workflows GitHub Actions :**
- `deploy.yml` : Build → GHCR push → Terraform apply (preprod auto, prod avec approval)
- `pr-preview.yml` : Env éphémère par PR (deploy on open, destroy on close, max 3)
- `pr.yml` : Tests backend + frontend + E2E

**Protections :**
- Azure Policy : allowlist de types de ressources + SKU PostgreSQL restreints
- RBAC : rôle custom "HouseFlow Deployer" (pas Contributor)
- Resource lock `CanNotDelete` sur le Resource Group
- `prevent_destroy` Terraform sur les ressources prod critiques

**Rôle « HouseFlow Deployer » :** porté par le service principal de l'app OIDC GitHub, assignable
au seul resource group `rg-houseflow`. Sa définition est versionnée dans
`infrastructure/rbac/houseflow-deployer.role.json` (l'ID de souscription y est un placeholder) —
c'est la source de vérité : toute ressource d'un nouveau type que Terraform doit créer commence
par une entrée dans ce fichier, puis :

```bash
az role definition update --role-definition "$(sed "s#<SUBSCRIPTION_ID>#$(az account show --query id -o tsv)#" infrastructure/rbac/houseflow-deployer.role.json)"
```

Le rôle ne couvre que le **plan de gestion**. Le plan de données Key Vault (certificats, secrets)
passe par les access policies que Terraform crée lui-même (`main/key-vault.tf`) — pas par RBAC.
Deux points à garder en tête :
- Le rôle étant limité au resource group, il ne peut pas agir sur les ressources de niveau
  souscription — dont les vaults en soft-delete (`Microsoft.KeyVault/locations/deletedVaults/*`).
  Les options `recover_soft_deleted_key_vaults` / `purge_soft_delete_on_destroy` du provider sont
  donc désactivées ; recréer un vault détruit depuis moins de 7 jours demande `az keyvault purge`
  par un administrateur.
- L'**allowlist Azure Policy** est gérée hors dépôt (portail) et doit contenir les types que le
  rôle autorise : pour Key Vault, `Microsoft.KeyVault/vaults` et
  `Microsoft.KeyVault/vaults/accessPolicies`. Un type manquant se manifeste par
  `RequestDisallowedByPolicy` à l'apply.
- Les **resource providers** doivent être enregistrés sur la souscription *avant* le premier
  apply d'un nouveau type : le provider Terraform ne le fait pas (`resource_provider_registrations
  = "none"`) et ne le pourrait pas — c'est une action de niveau souscription, hors du rôle. Un
  provider manquant se manifeste par `MissingSubscriptionRegistration` (HTTP 409). Ceux du
  projet :

  ```bash
  for ns in Microsoft.App Microsoft.Web Microsoft.DBforPostgreSQL Microsoft.OperationalInsights \
            Microsoft.Storage Microsoft.ManagedIdentity Microsoft.Network Microsoft.KeyVault; do
    az provider register --namespace "$ns"
  done
  az provider list --query "[?registrationState!='Registered' && namespace!=null].namespace" -o tsv
  ```

### DNS

**Domaine :** `houseflow.cloud`, enregistré et hébergé chez OVH.

**Convention de nommage :**
| Enregistrement | Usage |
|---|---|
| `www.houseflow.cloud` | Frontend prod |
| `api.houseflow.cloud` | API prod |
| `asuid.www.houseflow.cloud`, `asuid.api.houseflow.cloud` | TXT de validation Azure Container Apps (domaine custom + certificat géré) |
| `preprod.houseflow.cloud` | Frontend preprod |
| `api.preprod.houseflow.cloud` | API preprod |
| `asuid.preprod.houseflow.cloud`, `asuid.api.preprod.houseflow.cloud` | TXT de validation Azure Container Apps (domaine custom + certificat géré) |

Le frontend est sur `www.houseflow.cloud` / `preprod.houseflow.cloud` et non sur l'apex nu `houseflow.cloud` : Azure Container Apps valide les domaines custom par CNAME + TXT, et un CNAME ne peut pas coexister avec les enregistrements NS/SOA de l'apex d'une zone — OVH n'a pas de type ALIAS/ANAME pour contourner cette limite.

**Piloté par Terraform** (provider `ovh/ovh`) :
- Module réutilisable : `infrastructure/terraform/modules/ovh-dns-zone` (paramétré par zone + liste d'enregistrements)
- Configuration : `infrastructure/terraform/deploy-dns-ovh`, une seule instance du module pour la zone `houseflow.cloud` qui lit les FQDN Azure Container Apps depuis `deploy-prod.tfstate` (prod) et `deploy-preprod.tfstate` (preprod), et l'ID de vérification de domaine (commun aux deux, propriété du Container Apps Environment partagé `cae-houseflow`) depuis `deploy-prod.tfstate` (`terraform_remote_state`)
- Exécuté uniquement en CI (`.github/workflows/infra.yml`, jobs `plan-dns-ovh` / `apply-dns-ovh`) — jamais avec des credentials OVH en session interactive
- Le module ne gère jamais l'enregistrement racine (`""`) de la zone

**Redirection apex → www (hors Terraform) :** `houseflow.cloud` (apex nu) redirige vers `www.houseflow.cloud` via la redirection de domaine OVH, une fonctionnalité distincte de la zone DNS classique (endpoint `/domain/zone/{zone}/redirection`, pas `/record`). C'est une configuration **statique**, faite manuellement dans l'espace client OVH — le provider Terraform `ovh/ovh` ne l'expose pas, elle ne doit jamais être recréée ou modifiée par ce module.

**Création/rotation des credentials API OVH :**
1. Générer un token sur https://api.ovh.com/createToken/, avec 4 droits sur le chemin `/domain/zone/houseflow.cloud/*` : `GET`, `POST`, `PUT`, `DELETE`
2. Valider le Consumer Key via l'URL de confirmation renvoyée (connexion + 2FA)
3. Stocker `OVH_APPLICATION_SECRET` et `OVH_CONSUMER_KEY` en secrets GitHub du repo, `OVH_APPLICATION_KEY` en variable de repo
4. Pour une rotation : générer un nouveau token avec le même scope, mettre à jour les 3 valeurs, révoquer l'ancien token dans l'espace client OVH

**Ancien domaine décommissionné :** la prod servait auparavant depuis `houseflow.rouss.be` / `api.houseflow.rouss.be` (sous-domaine de `rouss.be`, hors zone gérée par Terraform). Ces enregistrements DNS restent temporairement en place chez OVH, gérés manuellement, le temps de vérifier `houseflow.cloud` en conditions réelles — à supprimer manuellement une fois cette vérification faite.

---

## Coûts estimés (MVP)

| Service | Coût |
|---------|------|
| Azure Container Apps | 0€ (tier gratuit) |
| Azure PostgreSQL (B1ms) | ~15€/mois |
| Log Analytics | ~2€/mois |
| **Total** | **~17€/mois** |

---
