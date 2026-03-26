# Guide de setup Azure pour HouseFlow

Checklist manuelle à réaliser avant de déployer l'infrastructure Terraform.

## Prérequis

- [ ] Souscription Azure active (le tier gratuit suffit pour Container Apps)
- [ ] Azure CLI installé (`brew install azure-cli` / `curl -sL https://aka.ms/InstallAzureCLIDeb | sudo bash`)
- [ ] Être connecté : `az login`

## 1. Azure AD — App Registration (Workload Identity Federation)

```bash
# Créer l'App Registration
az ad app create --display-name "houseflow-github-actions"

# Noter les valeurs suivantes :
# - Application (client) ID  → AZURE_CLIENT_ID
# - Directory (tenant) ID    → az account show --query tenantId -o tsv

# Créer le Service Principal associé
az ad sp create --id <AZURE_CLIENT_ID>
```

## 2. Federated Credentials (OIDC pour GitHub Actions)

Une credential par environnement GitHub Actions (le token OIDC utilise le nom de l'environnement, pas la branche).

```bash
# Credential pour les déploiements preprod
az ad app federated-credential create --id <AZURE_CLIENT_ID> --parameters '{
  "name": "github-actions-preprod",
  "issuer": "https://token.actions.githubusercontent.com",
  "subject": "repo:BarbeRouss/HouseFlow:environment:preprod",
  "audiences": ["api://AzureADTokenExchange"]
}'

# Credential pour les déploiements production
az ad app federated-credential create --id <AZURE_CLIENT_ID> --parameters '{
  "name": "github-actions-production",
  "issuer": "https://token.actions.githubusercontent.com",
  "subject": "repo:BarbeRouss/HouseFlow:environment:production",
  "audiences": ["api://AzureADTokenExchange"]
}'

# Credential pour les environnements éphémères (PRs)
az ad app federated-credential create --id <AZURE_CLIENT_ID> --parameters '{
  "name": "github-actions-preview",
  "issuer": "https://token.actions.githubusercontent.com",
  "subject": "repo:BarbeRouss/HouseFlow:environment:preview",
  "audiences": ["api://AzureADTokenExchange"]
}'
```

> **Via le portail Azure** : Entra ID → App registrations → houseflow-github-actions → Certificates & secrets → Federated credentials → + Add credential → GitHub Actions deploying Azure resources → Entity type: **Environment**

## 3. Resource Group + rôle RBAC custom

On utilise un **rôle custom** au lieu de Contributor pour limiter ce que GitHub Actions peut créer.

```bash
# Créer le Resource Group
az group create --name rg-houseflow --location westeurope

SUBSCRIPTION_ID=$(az account show --query id -o tsv)

# Créer le rôle custom (uniquement Container Apps, PostgreSQL, Logs, Storage)
az role definition create --role-definition "{
  \"Name\": \"HouseFlow Deployer\",
  \"Description\": \"Deploy Container Apps + PostgreSQL only — no VMs, no reserved instances\",
  \"Actions\": [
    \"Microsoft.App/*\",
    \"Microsoft.DBforPostgreSQL/flexibleServers/*\",
    \"Microsoft.OperationalInsights/workspaces/*\",
    \"Microsoft.Storage/storageAccounts/read\",
    \"Microsoft.Storage/storageAccounts/listKeys/action\",
    \"Microsoft.Storage/storageAccounts/blobServices/containers/*\",
    \"Microsoft.Resources/subscriptions/resourceGroups/read\",
    \"Microsoft.Resources/deployments/*\",
    \"Microsoft.Authorization/locks/*\",
    \"Microsoft.ManagedIdentity/userAssignedIdentities/*\"
  ],
  \"NotActions\": [],
  \"AssignableScopes\": [\"/subscriptions/$SUBSCRIPTION_ID/resourceGroups/rg-houseflow\"]
}"

# Assigner le rôle custom au Service Principal (PAS Contributor)
az role assignment create \
  --assignee <AZURE_CLIENT_ID> \
  --role "HouseFlow Deployer" \
  --scope /subscriptions/$SUBSCRIPTION_ID/resourceGroups/rg-houseflow
```

> **Pourquoi pas Contributor ?** Un Contributor peut créer n'importe quelle ressource Azure (VMs, reserved instances, Cosmos DB...). Le rôle custom limite strictement aux types de ressources dont HouseFlow a besoin.

## 4. Azure Policies — protection anti-dérapage

Ces policies s'appliquent **au niveau Azure** : même avec des credentials volées, les ressources non-autorisées sont **refusées à la création**.

### 4a. Allowlist des types de ressources

```bash
# Seuls ces types de ressources peuvent être créés dans le Resource Group
az policy assignment create \
  --name "houseflow-allowed-resources" \
  --display-name "HouseFlow — Types de ressources autorisés" \
  --policy "a08ec900-254a-4555-9bf5-e42af04b5c5c" \
  --scope "/subscriptions/$SUBSCRIPTION_ID/resourceGroups/rg-houseflow" \
  --params '{
    "listOfResourceTypesAllowed": {
      "value": [
        "Microsoft.App/containerApps",
        "Microsoft.App/managedEnvironments",
        "Microsoft.App/jobs",
        "Microsoft.DBforPostgreSQL/flexibleServers",
        "Microsoft.DBforPostgreSQL/flexibleServers/databases",
        "Microsoft.DBforPostgreSQL/flexibleServers/firewallRules",
        "Microsoft.DBforPostgreSQL/flexibleServers/configurations",
        "Microsoft.Storage/storageAccounts",
        "Microsoft.OperationalInsights/workspaces",
        "Microsoft.Authorization/locks",
        "Microsoft.ManagedIdentity/userAssignedIdentities"
      ]
    }
  }'
```

> Bloque : VMs, reserved instances, Cosmos DB, Synapse, Databricks, AKS, etc.

### 4b. Restriction des SKUs PostgreSQL

```bash
# Créer la policy definition (custom)
az policy definition create \
  --name "houseflow-pg-sku-restrict" \
  --display-name "HouseFlow — PostgreSQL SKU Burstable uniquement" \
  --mode "All" \
  --rules '{
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
  }' \
  --subscription "$SUBSCRIPTION_ID"

# Assigner la policy au Resource Group
az policy assignment create \
  --name "houseflow-pg-sku-restrict" \
  --display-name "HouseFlow — Bloquer PostgreSQL non-Burstable" \
  --policy "houseflow-pg-sku-restrict" \
  --scope "/subscriptions/$SUBSCRIPTION_ID/resourceGroups/rg-houseflow"
```

> Bloque : General Purpose (GP_Gen5), Memory Optimized, et tout SKU au-delà de ~30€/mois.

### 4c. Vérification des policies

```bash
# Lister les policies assignées au Resource Group
az policy assignment list \
  --scope "/subscriptions/$SUBSCRIPTION_ID/resourceGroups/rg-houseflow" \
  --query "[].{name:name, policy:displayName}" -o table

# Résultat attendu :
# Name                           Policy
# -----------------------------  -----------------------------------------
# houseflow-allowed-resources    HouseFlow — Types de ressources autorisés
# houseflow-pg-sku-restrict      HouseFlow — Bloquer PostgreSQL non-Burstable
```

## 5. Storage Account pour le Terraform State

```bash
az storage account create \
  --name sthouseflowtfstate \
  --resource-group rg-houseflow \
  --sku Standard_LRS \
  --location westeurope

az storage container create \
  --name tfstate \
  --account-name sthouseflowtfstate
```

## 6. PAT GitHub (pull GHCR depuis Azure)

1. Aller sur https://github.com/settings/tokens?type=beta (Fine-grained tokens)
2. Créer un token avec :
   - **Name** : `houseflow-azure-ghcr-pull`
   - **Expiration** : 1 an
   - **Repository access** : `BarbeRouss/HouseFlow` uniquement
   - **Permissions** : `Read access to packages` uniquement
3. Copier le token

## 7. Secrets GitHub Actions

Ajouter dans **Settings > Secrets and variables > Actions** du repo :

| Secret                   | Valeur                                       |
|--------------------------|----------------------------------------------|
| `AZURE_CLIENT_ID`        | Application (client) ID de l'App Registration |
| `AZURE_TENANT_ID`        | Directory (tenant) ID                         |
| `AZURE_SUBSCRIPTION_ID`  | `az account show --query id -o tsv`           |
| `GHCR_PAT`              | Le PAT fine-grained créé à l'étape 6          |

## 8. Vérification finale

```bash
SUBSCRIPTION_ID=$(az account show --query id -o tsv)

# App Registration
az ad app list --display-name "houseflow-github-actions" \
  --query "[].{id:appId, name:displayName}" -o table

# Resource Group
az group show --name rg-houseflow \
  --query "{name:name, location:location}" -o table

# Rôle custom
az role definition list --name "HouseFlow Deployer" \
  --query "[].{name:roleName, actions:permissions[0].actions}" -o table

# Policies
az policy assignment list \
  --scope "/subscriptions/$SUBSCRIPTION_ID/resourceGroups/rg-houseflow" \
  --query "[].{name:name, policy:displayName}" -o table

# Storage Account
az storage account show --name sthouseflowtfstate \
  --query "{name:name, sku:sku.name}" -o table
```

## Récapitulatif des protections

```
Couche 1 — GitHub       Branch protection + required review
Couche 2 — GitHub Actions   terraform plan visible avant apply
Couche 3 — Azure RBAC       Rôle custom (pas Contributor)
Couche 4 — Azure Policy      Allowlist de ressources + SKU PostgreSQL
Couche 5 — Budget            Alerte + kill switch à 25€/mois
```

Une fois toutes les cases cochées, le Terraform et les workflows GitHub Actions peuvent être déployés.
