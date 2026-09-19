# Guide de setup Azure pour HouseFlow

Checklist manuelle à réaliser avant le premier run du pipeline. Cible : `specs/infrastructure.md`
(source de vérité pour les noms, les subjects OIDC et les scopes — en cas de doute, c'est ce
document qui tranche, pas ce guide).

> Les commandes ci-dessous sont formatées pour **PowerShell**. Elles fonctionnent sur Windows, macOS et Linux.

## Prérequis

- [ ] Souscription Azure active (le tier gratuit suffit pour Container Apps)
- [ ] Azure CLI installé (`winget install Microsoft.AzureCLI` ou [instructions](https://learn.microsoft.com/en-us/cli/azure/install-azure-cli))
- [ ] GitHub CLI installé (`gh`) et authentifié : `gh auth login`
- [ ] Être connecté à Azure : `az login`
- [ ] Se placer à la racine du repo (les commandes ci-dessous référencent `infrastructure/rbac/` en chemin relatif)
- [ ] Enregistrer les Resource Providers nécessaires (une seule fois) :

```powershell
az provider register --namespace Microsoft.App
az provider register --namespace Microsoft.DBforPostgreSQL
az provider register --namespace Microsoft.OperationalInsights
az provider register --namespace Microsoft.Storage
az provider register --namespace Microsoft.ManagedIdentity
az provider register --namespace Microsoft.OperationsManagement
az provider register --namespace Microsoft.PolicyInsights
az provider register --namespace Microsoft.Network
az provider register --namespace Microsoft.KeyVault

# Vérifier (peut prendre quelques minutes par provider)
az provider list --query "[?contains('Microsoft.App Microsoft.DBforPostgreSQL Microsoft.OperationalInsights Microsoft.Storage Microsoft.ManagedIdentity Microsoft.OperationsManagement Microsoft.PolicyInsights Microsoft.Network Microsoft.KeyVault', namespace)].{namespace:namespace, state:registrationState}" -o table
```

> `Microsoft.KeyVault` n'est pas toujours pré-enregistré par défaut sur une souscription — le stack
> `shared` (Key Vault `kv-houseflow`) échoue sinon avec `MissingSubscriptionRegistration`.

## 1. Suppression de l'ancienne infrastructure (à faire une seule fois)

Aucun resource group n'existe plus, mais des traces de l'ancien monde (une seule app registration,
`rg-houseflow`) subsistent ailleurs et entrent en conflit avec le nouveau stack si elles ne sont pas
nettoyées d'abord — dans cet ordre précis.

```powershell
$SUBSCRIPTION_ID = az account show --query id -o tsv
$GITHUB_REPO = "BarbeRouss/HouseFlow"
```

### 1a. DNS OVH — détruire l'ancien stack Terraform

Le state de l'ancien stack DNS vit dans le storage account `sthouseflowtfstate` **de l'ancien**
`rg-houseflow`, qui va être supprimé à l'étape 1b : cette étape doit passer avant.

```powershell
cd infrastructure/terraform/deploy-dns-ovh
terraform init
terraform destroy
cd ../../..
```

> **Si le state n'est plus accessible** (storage déjà supprimé, backend cassé) : supprime les
> enregistrements manuellement dans l'espace client OVH, zone `houseflow.cloud` — `www`, `api`,
> `preprod`, `api-preprod`, `asuid.www`, `asuid.api`, `asuid.preprod`, `asuid.api-preprod`, et tous
> les `pr-<n>`, `api-pr-<n>`, `asuid.api-pr-<n>` restants. Sinon le nouveau stack DNS (module
> `ovh-dns-zone` + stack `dns`) entre en conflit à la création de ces mêmes enregistrements.

### 1b. Resource group

```powershell
az group delete --name rg-houseflow --yes
```

### 1c. App registration unique de l'ancien monde

```powershell
$oldAppId = az ad app list --display-name "houseflow-github-actions" --query "[0].appId" -o tsv
if ($oldAppId) { az ad app delete --id $oldAppId }
```

### 1d. Key Vault — purge définitive

`kv-houseflow` était dans `rg-houseflow` : sa suppression (étape 1b) l'a mis en soft-delete (7
jours). Le nom reste réservé tant qu'il n'est pas purgé — le prochain `terraform apply` du stack
`shared` échouera à la création sinon.

```powershell
az keyvault purge --name kv-houseflow --location westeurope
```

### 1e. Policies — suppression des deux assignments existants

Recréées à l'étape 8 avec l'allowlist mise à jour.

```powershell
az policy assignment delete --name "houseflow-allowed-resources" --scope "/subscriptions/$SUBSCRIPTION_ID"
az policy assignment delete --name "houseflow-pg-sku-restrict" --scope "/subscriptions/$SUBSCRIPTION_ID"
```

## 2. Resource groups

Quatre resource groups, tous en `westeurope` (une frontière = une identité — voir `specs/infrastructure.md`).

```powershell
$LOCATION = "westeurope"

az group create --name rg-houseflow-shared  --location $LOCATION
az group create --name rg-houseflow-preprod --location $LOCATION
az group create --name rg-houseflow-preview --location $LOCATION
az group create --name rg-houseflow-prod    --location $LOCATION
```

## 3. Azure AD — App Registrations (une par environnement)

Une **app registration** par environnement GitHub Actions applicatif (`preprod`, `preview`,
`prod` — `prod-approval` n'en a aucune, c'est une gate pure sans accès Azure).

- **App registration** = la définition de l'identité dans Entra ID (nom, credentials).
- **Service principal** = l'objet local au tenant qui reçoit effectivement les rôles RBAC — une
  app registration sans service principal ne peut se voir assigner aucun droit.
- **Une federated credential = une confiance, pas un droit.** Elle dit *qui* peut demander un
  token OIDC au nom de cette identité (étape 4) ; elle ne donne accès à aucune ressource Azure —
  ça, c'est le rôle du RBAC (étape 6).

```powershell
$AZURE_CLIENT_ID_PREPROD = az ad app create --display-name "houseflow-github-preprod" --query appId -o tsv
az ad sp create --id $AZURE_CLIENT_ID_PREPROD

$AZURE_CLIENT_ID_PREVIEW = az ad app create --display-name "houseflow-github-preview" --query appId -o tsv
az ad sp create --id $AZURE_CLIENT_ID_PREVIEW

$AZURE_CLIENT_ID_PROD = az ad app create --display-name "houseflow-github-prod" --query appId -o tsv
az ad sp create --id $AZURE_CLIENT_ID_PROD

# Identique pour les 3 — utilisé au secret de repo AZURE_TENANT_ID (étape 10)
az account show --query tenantId -o tsv
```

## 4. Federated Credentials (OIDC pour GitHub Actions)

Exactement une credential par app registration, sur le subject de son environnement GitHub (le
token OIDC porte le nom de l'environnement, pas la branche).

```powershell
az ad app federated-credential create --id $AZURE_CLIENT_ID_PREPROD --parameters '@{
  "name": "github-actions-preprod",
  "issuer": "https://token.actions.githubusercontent.com",
  "subject": "repo:BarbeRouss/HouseFlow:environment:preprod",
  "audiences": ["api://AzureADTokenExchange"]
}'@

az ad app federated-credential create --id $AZURE_CLIENT_ID_PREVIEW --parameters '@{
  "name": "github-actions-preview",
  "issuer": "https://token.actions.githubusercontent.com",
  "subject": "repo:BarbeRouss/HouseFlow:environment:preview",
  "audiences": ["api://AzureADTokenExchange"]
}'@

az ad app federated-credential create --id $AZURE_CLIENT_ID_PROD --parameters '@{
  "name": "github-actions-prod",
  "issuer": "https://token.actions.githubusercontent.com",
  "subject": "repo:BarbeRouss/HouseFlow:environment:prod",
  "audiences": ["api://AzureADTokenExchange"]
}'@
```

> **Alternative via le portail Azure** : Entra ID → App registrations → (l'app de l'environnement)
> → Certificates & secrets → Federated credentials → + Add credential → GitHub Actions deploying
> Azure resources → Entity type: **Environment**.

## 5. Rôles RBAC custom

On utilise des **rôles custom** au lieu de Contributor pour limiter strictement ce que chaque
identité peut créer. Définitions versionnées dans `infrastructure/rbac/` (source de vérité — l'ID
de souscription y est le placeholder `<SUBSCRIPTION_ID>`) :

| Rôle | Fichier | Contenu |
|---|---|---|
| `HouseFlow Deployer` | `houseflow-deployer.role.json` | plan de gestion des types que Terraform crée dans un RG d'environnement : Container Apps (apps, environnements, jobs, certificats), Static Web Apps, PostgreSQL, Log Analytics, Storage, Network (VNet, subnets, peerings), Identity (assign/action), Key Vault, locks. Pas de `roleAssignments/write`. |
| `HouseFlow Shared Tenant` | `houseflow-shared-tenant.role.json` | ce qu'un environnement non-prod (preprod, preview) a le droit de faire dans `rg-houseflow-shared`, et rien d'autre : peering VNet, lien de la private DNS zone, lecture des identités managées / de PostgreSQL / du Key Vault / du storage account. |
| `HouseFlow Deployer (subscription)` | `houseflow-deployer-subscription.role.json` | `Microsoft.Web/locations/*/read` uniquement — lecture de l'état des opérations longues de Static Web Apps (domaine custom des previews de PR), publié par Azure hors resource group. |

```powershell
$SUBSCRIPTION_ID = az account show --query id -o tsv

# HouseFlow Deployer
$roleDefinition = (Get-Content infrastructure/rbac/houseflow-deployer.role.json -Raw) -replace "<SUBSCRIPTION_ID>", $SUBSCRIPTION_ID
$roleDefinition | Out-File -Encoding utf8 role-definition.json
az role definition create --role-definition role-definition.json
Remove-Item role-definition.json

# HouseFlow Shared Tenant
$roleDefinition = (Get-Content infrastructure/rbac/houseflow-shared-tenant.role.json -Raw) -replace "<SUBSCRIPTION_ID>", $SUBSCRIPTION_ID
$roleDefinition | Out-File -Encoding utf8 role-definition.json
az role definition create --role-definition role-definition.json
Remove-Item role-definition.json
```

> `assignableScopes` de chaque JSON couvre déjà les scopes des assignations de l'étape 6 (les
> quatre resource groups pour `HouseFlow Deployer`, `rg-houseflow-shared` pour `Shared Tenant`).

Le rôle souscription (lecture de l'état des opérations longues des Static Web Apps) ne sert qu'à
**preview** ; le script idempotent le crée et l'assigne :

```powershell
pwsh infrastructure/rbac/Assign-DeployerSubscriptionRole.ps1 -SubscriptionId $SUBSCRIPTION_ID
```

> **Mise à jour d'un rôle existant** : modifier le JSON versionné, puis `az role definition update` :
> ```powershell
> az role definition update --role-definition "$((Get-Content infrastructure/rbac/houseflow-deployer.role.json -Raw) -replace '<SUBSCRIPTION_ID>', $SUBSCRIPTION_ID)"
> ```
> (même chose pour `houseflow-shared-tenant.role.json`). Un nouveau type de ressource se manifeste
> à l'apply par `AuthorizationFailed` : ajouter l'action au JSON, mettre à jour le rôle, et
> enregistrer le resource provider s'il est nouveau (`az provider register --namespace …`, action
> de niveau souscription que le rôle ne peut pas faire).

> **Pourquoi pas Contributor ?** Un Contributor peut créer n'importe quelle ressource Azure (VMs,
> reserved instances, Cosmos DB...). Les rôles custom limitent strictement aux types de ressources
> dont HouseFlow a besoin, par frontière.

> **Pourquoi `Microsoft.Web/locations/*/read` et pas l'action exacte ?** Le contrôle d'autorisation
> réclame `Microsoft.Web/locations/staticSitesOperationStatuses/read`, mais cette action n'est pas
> publiée dans le registre d'opérations du provider : `az role definition create` la refuse
> (`InvalidActionOrNotAction`). Le wildcard passe la validation et couvre l'action à l'évaluation.

## 6. Role assignments

Strictement le tableau « Identités et RBAC » de `specs/infrastructure.md` :

| Environnement | Rôle | Scope |
|---|---|---|
| `preprod` | `HouseFlow Deployer` | `rg-houseflow-preprod` |
| `preprod` | `HouseFlow Shared Tenant` | `rg-houseflow-shared` |
| `preview` | `HouseFlow Deployer` | `rg-houseflow-preview` |
| `preview` | `HouseFlow Shared Tenant` | `rg-houseflow-shared` |
| `preview` | `HouseFlow Deployer (subscription)` | souscription (fait à l'étape 5) |
| `prod` | `HouseFlow Deployer` | `rg-houseflow-prod` **et** `rg-houseflow-shared` |
| `prod` | `Role Based Access Control Administrator` (conditionné ABAC) | `rg-houseflow-shared` |
| `prod` | `Key Vault Certificates Officer` + `Key Vault Secrets Officer` | `kv-houseflow` (**après** le premier apply du stack `shared` — le vault n'existe pas avant) |

```powershell
# preprod
az role assignment create --assignee $AZURE_CLIENT_ID_PREPROD --role "HouseFlow Deployer" `
  --scope "/subscriptions/$SUBSCRIPTION_ID/resourceGroups/rg-houseflow-preprod"
az role assignment create --assignee $AZURE_CLIENT_ID_PREPROD --role "HouseFlow Shared Tenant" `
  --scope "/subscriptions/$SUBSCRIPTION_ID/resourceGroups/rg-houseflow-shared"

# preview
az role assignment create --assignee $AZURE_CLIENT_ID_PREVIEW --role "HouseFlow Deployer" `
  --scope "/subscriptions/$SUBSCRIPTION_ID/resourceGroups/rg-houseflow-preview"
az role assignment create --assignee $AZURE_CLIENT_ID_PREVIEW --role "HouseFlow Shared Tenant" `
  --scope "/subscriptions/$SUBSCRIPTION_ID/resourceGroups/rg-houseflow-shared"

# prod
az role assignment create --assignee $AZURE_CLIENT_ID_PROD --role "HouseFlow Deployer" `
  --scope "/subscriptions/$SUBSCRIPTION_ID/resourceGroups/rg-houseflow-prod"
az role assignment create --assignee $AZURE_CLIENT_ID_PROD --role "HouseFlow Deployer" `
  --scope "/subscriptions/$SUBSCRIPTION_ID/resourceGroups/rg-houseflow-shared"
```

### 6a. `prod` — Role Based Access Control Administrator conditionné (ABAC)

`prod` doit pouvoir assigner du RBAC dans `rg-houseflow-shared` (Key Vault, blob) aux identités
managées créées par le stack `shared`, sans pouvoir s'octroyer — ni octroyer à personne — un rôle
plus large. Une condition ABAC restreint les rôles assignables/révocables aux quatre rôles
data-plane utilisés par ces identités :

```powershell
$kvSecretsUser = "4633458b-17de-408a-b874-0445c86b69e6"   # Key Vault Secrets User
$kvCertOfficer = "a4417e6f-fecd-4de8-b567-7b0420556985"   # Key Vault Certificates Officer
$blobReader    = "2a2b9908-6ea1-4ae2-8e65-a410df84e7d1"   # Storage Blob Data Reader
$blobContrib   = "ba92f5b4-2d11-453d-a403-e96b0029c9fe"   # Storage Blob Data Contributor

$condition = "((!(ActionMatches{'Microsoft.Authorization/roleAssignments/write'})) OR " +
  "(@Request[Microsoft.Authorization/roleAssignments:RoleDefinitionId] ForAnyOfAnyValues:GuidEquals {$kvSecretsUser, $kvCertOfficer, $blobReader, $blobContrib}))" +
  " AND " +
  "((!(ActionMatches{'Microsoft.Authorization/roleAssignments/delete'})) OR " +
  "(@Resource[Microsoft.Authorization/roleAssignments:RoleDefinitionId] ForAnyOfAnyValues:GuidEquals {$kvSecretsUser, $kvCertOfficer, $blobReader, $blobContrib}))"

az role assignment create `
  --assignee $AZURE_CLIENT_ID_PROD `
  --role "Role Based Access Control Administrator" `
  --scope "/subscriptions/$SUBSCRIPTION_ID/resourceGroups/rg-houseflow-shared" `
  --condition $condition `
  --condition-version "2.0"
```

### 6b. `prod` — rôles Key Vault sur `kv-houseflow` (après le premier apply)

⚠️ **Ne pas exécuter maintenant.** `kv-houseflow` n'existe pas avant le premier `terraform apply`
du stack `shared` (voir étape 11). Revenir ici juste après :

```powershell
$kvScope = "/subscriptions/$SUBSCRIPTION_ID/resourceGroups/rg-houseflow-shared/providers/Microsoft.KeyVault/vaults/kv-houseflow"

az role assignment create --assignee $AZURE_CLIENT_ID_PROD --role "Key Vault Certificates Officer" --scope $kvScope
az role assignment create --assignee $AZURE_CLIENT_ID_PROD --role "Key Vault Secrets Officer" --scope $kvScope
```

## 7. Storage Account pour le Terraform state

Un seul storage account, dans `rg-houseflow-shared`, trois conteneurs (un par frontière d'écriture
— voir `specs/infrastructure.md`).

```powershell
az storage account create `
  --name sthouseflowtfstate `
  --resource-group rg-houseflow-shared `
  --sku Standard_LRS `
  --location $LOCATION `
  --allow-blob-public-access false `
  --min-tls-version TLS1_2

az storage container create --name tfstate-shared  --account-name sthouseflowtfstate --auth-mode login
az storage container create --name tfstate-nonprod --account-name sthouseflowtfstate --auth-mode login
az storage container create --name tfstate-prod    --account-name sthouseflowtfstate --auth-mode login
```

`Storage Blob Data Contributor` **par conteneur**, selon qui écrit quel state :

| Conteneur | États | Écrit par |
|---|---|---|
| `tfstate-shared` | `shared.tfstate`, `dns.tfstate` | `prod` |
| `tfstate-prod` | `env-prod.tfstate`, `deploy-prod.tfstate` | `prod` |
| `tfstate-nonprod` | `env-preprod.tfstate`, `deploy-preprod.tfstate`, `env-preview.tfstate`, `ephemeral-pr-<n>.tfstate` | `preprod`, `preview` |

```powershell
$storageScope = "/subscriptions/$SUBSCRIPTION_ID/resourceGroups/rg-houseflow-shared/providers/Microsoft.Storage/storageAccounts/sthouseflowtfstate/blobServices/default/containers"

az role assignment create --assignee $AZURE_CLIENT_ID_PROD --role "Storage Blob Data Contributor" --scope "$storageScope/tfstate-shared"
az role assignment create --assignee $AZURE_CLIENT_ID_PROD --role "Storage Blob Data Contributor" --scope "$storageScope/tfstate-prod"

az role assignment create --assignee $AZURE_CLIENT_ID_PREPROD --role "Storage Blob Data Contributor" --scope "$storageScope/tfstate-nonprod"
az role assignment create --assignee $AZURE_CLIENT_ID_PREVIEW --role "Storage Blob Data Contributor" --scope "$storageScope/tfstate-nonprod"
```

## 8. Azure Policies — protection anti-dérapage (niveau souscription)

Ces policies s'appliquent **au niveau de la souscription** : elles couvrent les quatre resource
groups (actuels et futurs). Même avec des credentials volées, les ressources non-autorisées sont
**refusées à la création**.

> **Souscription partagée ?** Si d'autres projets existent sur la même souscription, ajoute des
> **exclusions** sur leurs Resource Groups lors de l'assignment (champ `--not-scopes` en CLI, ou
> "Exclusions" dans le portail).

### 8a. Allowlist des types de ressources

```powershell
$allowedResourcesParams = @"
{
  "listOfResourceTypesAllowed": {
    "value": [
      "Microsoft.App/containerApps",
      "Microsoft.App/containerApps/revisions",
      "Microsoft.App/managedEnvironments",
      "Microsoft.App/managedEnvironments/certificates",
      "Microsoft.App/managedEnvironments/managedCertificates",
      "Microsoft.App/managedEnvironments/storages",
      "Microsoft.App/jobs",
      "Microsoft.Web/staticSites",
      "Microsoft.DBforPostgreSQL/flexibleServers",
      "Microsoft.DBforPostgreSQL/flexibleServers/databases",
      "Microsoft.DBforPostgreSQL/flexibleServers/firewallRules",
      "Microsoft.DBforPostgreSQL/flexibleServers/configurations",
      "Microsoft.DBforPostgreSQL/flexibleServers/administrators",
      "Microsoft.Storage/storageAccounts",
      "Microsoft.OperationalInsights/workspaces",
      "Microsoft.Authorization/locks",
      "Microsoft.ManagedIdentity/userAssignedIdentities",
      "Microsoft.Resources/resourceGroups",
      "Microsoft.KeyVault/vaults",
      "Microsoft.Network/virtualNetworks",
      "Microsoft.Network/virtualNetworks/subnets",
      "Microsoft.Network/virtualNetworks/virtualNetworkPeerings",
      "Microsoft.Network/privateDnsZones",
      "Microsoft.Network/privateDnsZones/virtualNetworkLinks",
      "Microsoft.Network/networkSecurityGroups",
      "Microsoft.Network/publicIPAddresses",
      "Microsoft.Network/loadBalancers"
    ]
  }
}
"@

$allowedResourcesParams | Out-File -Encoding utf8 allowed-resources-params.json

az policy assignment create `
  --name "houseflow-allowed-resources" `
  --display-name "HouseFlow - Types de ressources autorises" `
  --policy "a08ec900-254a-4555-9bf5-e42af04b5c5c" `
  --scope "/subscriptions/$SUBSCRIPTION_ID" `
  --params allowed-resources-params.json

Remove-Item allowed-resources-params.json
```

> Bloque : VMs, reserved instances, Cosmos DB, Synapse, Databricks, AKS, etc.
> `Microsoft.Resources/resourceGroups` permet la gestion des RG eux-mêmes. `Microsoft.Web/staticSites`
> est nécessaire aux previews de PR (`infrastructure/terraform/modules/ephemeral-env/main.tf` crée
> une `azurerm_static_web_app`, qui correspond à ce type ARM). `Microsoft.KeyVault/vaults` est
> nécessaire au stack `shared`.

**Via le portail** : Policy → Assignments → + Assign policy → Scope = **souscription** → cherche
"Allowed resource types" → Parameters → coche les types ci-dessus.

> **Mise à jour d'une policy existante** : supprimer et recréer l'assignment :
> ```powershell
> az policy assignment delete --name "houseflow-allowed-resources" --scope "/subscriptions/$SUBSCRIPTION_ID"
> # Puis relancer la commande az policy assignment create ci-dessus
> ```

### 8b. Restriction des SKUs PostgreSQL

```powershell
$policyRules = @"
{
  "if": {
    "allOf": [
      {
        "field": "type",
        "equals": "Microsoft.DBforPostgreSQL/flexibleServers"
      },
      {
        "not": {
          "field": "Microsoft.DBforPostgreSQL/flexibleServers/sku.name",
          "in": ["Standard_B1ms", "Standard_B2s"]
        }
      }
    ]
  },
  "then": { "effect": "deny" }
}
"@

$policyRules | Out-File -Encoding utf8 pg-sku-rules.json

az policy definition create `
  --name "houseflow-pg-sku-restrict" `
  --display-name "HouseFlow - PostgreSQL SKU Burstable uniquement" `
  --mode "All" `
  --rules pg-sku-rules.json `
  --subscription $SUBSCRIPTION_ID

Remove-Item pg-sku-rules.json

az policy assignment create `
  --name "houseflow-pg-sku-restrict" `
  --display-name "HouseFlow - Bloquer PostgreSQL non-Burstable" `
  --policy "houseflow-pg-sku-restrict" `
  --scope "/subscriptions/$SUBSCRIPTION_ID"
```

> Bloque : General Purpose (GP_Gen5), Memory Optimized, et tout SKU au-delà de ~30 EUR/mois.
> `psql-houseflow` (le seul serveur, mutualisé) est `B_Standard_B1ms`.

### 8c. Vérification des policies

```powershell
az policy assignment list `
  --scope "/subscriptions/$SUBSCRIPTION_ID" `
  --query "[?contains(name, 'houseflow')].{name:name, policy:displayName}" -o table

# Résultat attendu :
# Name                           Policy
# -----------------------------  -----------------------------------------
# houseflow-allowed-resources    HouseFlow - Types de ressources autorises
# houseflow-pg-sku-restrict      HouseFlow - Bloquer PostgreSQL non-Burstable
```

## 9. Credentials externes

### 9a. PAT GitHub (pull GHCR depuis Azure)

1. Aller sur https://github.com/settings/tokens/new (Classic token — les Fine-grained tokens ne supportent pas `packages`)
2. Créer un token avec :
   - **Note** : `houseflow-azure-ghcr-pull`
   - **Expiration** : Custom → 1 an
   - **Scopes** : cocher uniquement **`read:packages`**
3. **Generate token** → copier le token

### 9b. Object ID Entra (pour l'accès DB via Entra ID)

```powershell
# Votre Object ID (compte Microsoft connecté)
$ENTRA_OBJECT_ID = az ad signed-in-user show --query id -o tsv
echo "Object ID: $ENTRA_OBJECT_ID"

# Votre nom d'affichage
$ENTRA_NAME = az ad signed-in-user show --query userPrincipalName -o tsv
echo "Name: $ENTRA_NAME"
```

> Ces valeurs sont utilisées par Terraform pour vous ajouter comme admin Entra sur PostgreSQL.
> Cela vous permet de vous connecter à la DB sans mot de passe via `az login` + `psql`.

### 9c. Credentials API OVH (DNS-01)

Le job `certificate` et le stack `dns` s'authentifient auprès de l'API OVH pour gérer la zone
`houseflow.cloud` :

1. Générer un token sur https://api.ovh.com/createToken/, avec 4 droits sur le chemin
   `/domain/zone/houseflow.cloud/*` : `GET`, `POST`, `PUT`, `DELETE`
2. Valider le Consumer Key via l'URL de confirmation renvoyée (connexion + 2FA)
3. Noter les trois valeurs (Application Key, Application Secret, Consumer Key) — elles vont aux
   secrets/variables de repo à l'étape 10

## 10. GitHub — environnements et secrets

### 10a. Environnements

Quatre environnements : `preprod`, `preview`, `prod` (sans reviewers — la protection vient de la
gate `prod-approval`) et `prod-approval` (required reviewers = le mainteneur, déploiement limité à
`main`).

**Via l'UI** : Settings → Environments → New environment.
- `preprod`, `preview`, `prod` : créer, ne rien configurer d'autre.
- `prod-approval` : Required reviewers → ajouter le mainteneur ; Deployment branches and tags →
  Selected branches → Add rule → `main`.

**Équivalent `gh api`** :

```powershell
gh api --method PUT "repos/$GITHUB_REPO/environments/preprod" | Out-Null
gh api --method PUT "repos/$GITHUB_REPO/environments/preview" | Out-Null
gh api --method PUT "repos/$GITHUB_REPO/environments/prod" | Out-Null

# prod-approval : reviewer = le mainteneur (id numérique, pas le login)
$maintainerId = gh api users/BarbeRouss --jq ".id"

$prodApprovalBody = @{
  reviewers = @(@{ type = "User"; id = [int]$maintainerId })
  deployment_branch_policy = @{ protected_branches = $false; custom_branch_policies = $true }
} | ConvertTo-Json -Depth 5

$prodApprovalBody | Out-File -Encoding utf8 prod-approval-env.json
gh api --method PUT "repos/$GITHUB_REPO/environments/prod-approval" --input prod-approval-env.json
Remove-Item prod-approval-env.json

gh api --method POST "repos/$GITHUB_REPO/environments/prod-approval/deployment-branch-policies" -f name='main'
```

### 10b. Secrets d'environnement

| Environnement | Secret | Valeur |
|---|---|---|
| `preprod` | `AZURE_CLIENT_ID` | `$AZURE_CLIENT_ID_PREPROD` |
| `preprod` | `JWT_KEY` | clé JWT dédiée preprod (32+ caractères, différente de prod et preview) |
| `preprod` | `BASTION_SSH_PUBLIC_KEY` | contenu de `~/.ssh/id_ed25519.pub` (bastion preprod) |
| `preview` | `AZURE_CLIENT_ID` | `$AZURE_CLIENT_ID_PREVIEW` |
| `preview` | `JWT_KEY` | clé JWT dédiée preview |
| `prod` | `AZURE_CLIENT_ID` | `$AZURE_CLIENT_ID_PROD` |
| `prod` | `JWT_KEY` | clé JWT dédiée prod |
| `prod` | `BASTION_SSH_PUBLIC_KEY` | contenu de `~/.ssh/id_ed25519.pub` (bastion prod) |

`preview` n'a pas de `BASTION_SSH_PUBLIC_KEY` : les previews de PR n'ont pas de bastion (accès DB
via le job `dbtools`, pas de debug interactif).

```powershell
gh secret set AZURE_CLIENT_ID --repo $GITHUB_REPO --env preprod --body $AZURE_CLIENT_ID_PREPROD
gh secret set AZURE_CLIENT_ID --repo $GITHUB_REPO --env preview --body $AZURE_CLIENT_ID_PREVIEW
gh secret set AZURE_CLIENT_ID --repo $GITHUB_REPO --env prod    --body $AZURE_CLIENT_ID_PROD

gh secret set JWT_KEY --repo $GITHUB_REPO --env preprod
gh secret set JWT_KEY --repo $GITHUB_REPO --env preview
gh secret set JWT_KEY --repo $GITHUB_REPO --env prod

gh secret set BASTION_SSH_PUBLIC_KEY --repo $GITHUB_REPO --env preprod --body (Get-Content ~/.ssh/id_ed25519.pub -Raw)
gh secret set BASTION_SSH_PUBLIC_KEY --repo $GITHUB_REPO --env prod    --body (Get-Content ~/.ssh/id_ed25519.pub -Raw)
```

> `gh secret set NAME` sans `--body` ouvre un prompt interactif — pratique pour coller une clé
> générée sans la laisser dans l'historique du shell.

### 10c. Secrets et variables de repo

| Secret (repo) | Valeur |
|---|---|
| `AZURE_TENANT_ID` | Directory (tenant) ID — étape 3 |
| `AZURE_SUBSCRIPTION_ID` | `$SUBSCRIPTION_ID` |
| `GHCR_PAT` | le PAT Classic de l'étape 9a |
| `OVH_APPLICATION_SECRET` | Application Secret OVH — étape 9c |
| `OVH_CONSUMER_KEY` | Consumer Key OVH — étape 9c |
| `ENTRA_ADMIN_OBJECT_ID` | `$ENTRA_OBJECT_ID` — étape 9b |
| `ENTRA_ADMIN_NAME` | `$ENTRA_NAME` — étape 9b |

| Variable (repo) | Valeur |
|---|---|
| `OVH_APPLICATION_KEY` | Application Key OVH — étape 9c |
| `LETSENCRYPT_EMAIL` | adresse de contact pour le compte ACME (ex. celle du mainteneur) |

```powershell
$TENANT_ID = az account show --query tenantId -o tsv

gh secret set AZURE_TENANT_ID --repo $GITHUB_REPO --body $TENANT_ID
gh secret set AZURE_SUBSCRIPTION_ID --repo $GITHUB_REPO --body $SUBSCRIPTION_ID
gh secret set GHCR_PAT --repo $GITHUB_REPO
gh secret set OVH_APPLICATION_SECRET --repo $GITHUB_REPO
gh secret set OVH_CONSUMER_KEY --repo $GITHUB_REPO
gh secret set ENTRA_ADMIN_OBJECT_ID --repo $GITHUB_REPO --body $ENTRA_OBJECT_ID
gh secret set ENTRA_ADMIN_NAME --repo $GITHUB_REPO --body $ENTRA_NAME

gh variable set OVH_APPLICATION_KEY --repo $GITHUB_REPO
gh variable set LETSENCRYPT_EMAIL --repo $GITHUB_REPO --body "admin@houseflow.cloud"
```

## 11. Vérification finale et premier run

### 11a. Vérification

```powershell
$SUBSCRIPTION_ID = az account show --query id -o tsv

# App Registrations
az ad app list --query "[?starts_with(displayName, 'houseflow-github-')].{id:appId, name:displayName}" -o table

# Federated Credentials (une par app)
az ad app federated-credential list --id $AZURE_CLIENT_ID_PREPROD -o table
az ad app federated-credential list --id $AZURE_CLIENT_ID_PREVIEW -o table
az ad app federated-credential list --id $AZURE_CLIENT_ID_PROD -o table

# Resource groups
az group list --query "[?starts_with(name, 'rg-houseflow-')].{name:name, location:location}" -o table

# Rôles custom
az role definition list --custom-role-only true `
  --query "[?starts_with(roleName, 'HouseFlow')].{name:roleName}" -o table

# Role assignments (les 3 identités)
az role assignment list --assignee $AZURE_CLIENT_ID_PREPROD -o table
az role assignment list --assignee $AZURE_CLIENT_ID_PREVIEW -o table
az role assignment list --assignee $AZURE_CLIENT_ID_PROD -o table

# Policies
az policy assignment list `
  --scope "/subscriptions/$SUBSCRIPTION_ID" `
  --query "[?contains(name, 'houseflow')].{name:name, policy:displayName}" -o table

# Storage
az storage account show --name sthouseflowtfstate --query "{name:name, sku:sku.name}" -o table
az storage container list --account-name sthouseflowtfstate --auth-mode login -o table
```

### 11b. Premier run

```powershell
gh workflow run pipeline.yml --repo $GITHUB_REPO --ref main -f force_infra=true
```

`shared`, `env-prod`, `env-preprod` et `env-preview` sont tous concernés par `force_infra=true` :
une seule approbation est attendue, sur `prod-approval` (job `approve-infra`) — approuve le
déploiement en attente depuis l'onglet **Actions** du run, ou :

```powershell
gh run list --repo $GITHUB_REPO --workflow pipeline.yml --limit 1
gh run view <run-id> --repo $GITHUB_REPO
```

Une fois le run vert (le stack `shared` est appliqué, `kv-houseflow` existe) :

1. Reviens à l'**étape 6b** ci-dessus et exécute les deux `az role assignment create` sur
   `kv-houseflow` (impossible avant, le vault n'existait pas).
2. Relance le job certificat, maintenant que les rôles Key Vault sont en place :

```powershell
gh workflow run pipeline.yml --repo $GITHUB_REPO --ref main -f force_certificate=true
```

## 12. Se connecter à PostgreSQL (debug via DBeaver / psql)

La DB est dans un VNet privé (pas d'accès public). Un **Container App bastion** (scale-to-zero),
un par environnement (`ca-bastion-preprod`, `ca-bastion-prod` — pas de bastion pour preview), fait
office de tunnel SSH.

### Prérequis

- Clé SSH configurée (la clé publique doit être dans le secret d'environnement `BASTION_SSH_PUBLIC_KEY` correspondant)
- Le bastion scale à zéro — la première connexion prend ~30s (cold start)
- Le FQDN du bastion est un output du stack `env-preprod` ou `env-prod` : `terraform output bastion_fqdn` dans le dossier correspondant

### Via SSH tunnel (ligne de commande)

```powershell
# 1. Obtenir un token Entra pour PostgreSQL
$token = az account get-access-token `
  --resource-type oss-rdbms `
  --query accessToken -o tsv

# 2. Ouvrir le tunnel SSH (port local 5432 → PostgreSQL privé)
# Remplacer <bastion_fqdn> par le FQDN du bastion de l'environnement visé (terraform output)
ssh -N -L 5432:psql-houseflow.houseflow.private.postgres.database.azure.com:5432 `
  bastion@<bastion_fqdn> -p 2222

# 3. Dans un autre terminal : se connecter (houseflow_preprod ou houseflow_prod selon le bastion utilisé)
$env:PGPASSWORD = $token
psql "host=localhost port=5432 dbname=houseflow_preprod user=<votre-email> sslmode=require"
```

### Via DBeaver

1. **Onglet SSH** de la connexion :
   - Host : `<bastion_fqdn>` (output Terraform `bastion_fqdn` du stack `env-preprod` ou `env-prod`)
   - Port : `2222`
   - User : `bastion`
   - Authentication : Public Key → sélectionner votre clé privée (`~/.ssh/id_ed25519`)

2. **Onglet Main** :
   - Host : `psql-houseflow.houseflow.private.postgres.database.azure.com`
   - Port : `5432`
   - Database : `houseflow_preprod` (ou `houseflow_prod`, selon le bastion utilisé)
   - Username : votre email Microsoft (ex: `user@domain.com`)
   - Password : le token obtenu via `az account get-access-token --resource-type oss-rdbms --query accessToken -o tsv`

> **Note** : le token Entra expire après ~1h. Regénérez-le si la connexion échoue.

## Récapitulatif des protections

```
Couche 0 — Identités        Un service principal par environnement, RBAC par resource group
Couche 1 — GitHub           Branch protection + required review
Couche 2 — GitHub Actions   terraform plan visible avant apply
Couche 3 — Azure RBAC       Rôles custom (pas Contributor), un par frontière
Couche 4 — Azure Policy     Allowlist de ressources + SKU PostgreSQL
Couche 5 — Budget           Alerte + kill switch à 25 EUR/mois
Couche 6 — Réseau           VNet privé par environnement, peering vers shared, PostgreSQL sans accès public
Couche 7 — Auth             Entra ID (passwordless), pas de secrets DB
```
