# Guide de setup Azure pour HouseFlow

Checklist manuelle à réaliser avant de déployer l'infrastructure Terraform.

> Les commandes ci-dessous sont formatées pour **PowerShell**. Elles fonctionnent sur Windows, macOS et Linux.

## Prérequis

- [ ] Souscription Azure active (le tier gratuit suffit pour Container Apps)
- [ ] Azure CLI installé (`winget install Microsoft.AzureCLI` ou [instructions](https://learn.microsoft.com/en-us/cli/azure/install-azure-cli))
- [ ] Être connecté : `az login`
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

# Vérifier (peut prendre quelques minutes par provider)
az provider list --query "[?contains('Microsoft.App Microsoft.DBforPostgreSQL Microsoft.OperationalInsights Microsoft.Storage Microsoft.ManagedIdentity Microsoft.OperationsManagement Microsoft.PolicyInsights Microsoft.Network', namespace)].{namespace:namespace, state:registrationState}" -o table
```

## 1. Azure AD — App Registration (Workload Identity Federation)

```powershell
# Créer l'App Registration
az ad app create --display-name "houseflow-github-actions"

# Noter les valeurs suivantes :
# - Application (client) ID  → AZURE_CLIENT_ID
# - Directory (tenant) ID :
az account show --query tenantId -o tsv

# Stocker le Client ID pour les commandes suivantes
$AZURE_CLIENT_ID = "<coller ici l'Application (client) ID>"

# Créer le Service Principal associé
az ad sp create --id $AZURE_CLIENT_ID
```

## 2. Federated Credentials (OIDC pour GitHub Actions)

Une credential par environnement GitHub Actions (le token OIDC utilise le nom de l'environnement, pas la branche).

```powershell
# Credential pour les déploiements preprod
az ad app federated-credential create --id $AZURE_CLIENT_ID --parameters '@{
  "name": "github-actions-preprod",
  "issuer": "https://token.actions.githubusercontent.com",
  "subject": "repo:BarbeRouss/HouseFlow:environment:preprod",
  "audiences": ["api://AzureADTokenExchange"]
}'@

# Credential pour les déploiements production
az ad app federated-credential create --id $AZURE_CLIENT_ID --parameters '@{
  "name": "github-actions-production",
  "issuer": "https://token.actions.githubusercontent.com",
  "subject": "repo:BarbeRouss/HouseFlow:environment:production",
  "audiences": ["api://AzureADTokenExchange"]
}'@

# Credential pour les environnements éphémères (PRs)
az ad app federated-credential create --id $AZURE_CLIENT_ID --parameters '@{
  "name": "github-actions-preview",
  "issuer": "https://token.actions.githubusercontent.com",
  "subject": "repo:BarbeRouss/HouseFlow:environment:preview",
  "audiences": ["api://AzureADTokenExchange"]
}'@
```

> **Alternative via le portail Azure** : Entra ID → App registrations → houseflow-github-actions → Certificates & secrets → Federated credentials → + Add credential → GitHub Actions deploying Azure resources → Entity type: **Environment**

## 3. Resource Group + rôle RBAC custom

On utilise un **rôle custom** au lieu de Contributor pour limiter ce que GitHub Actions peut créer.

```powershell
# Créer le Resource Group
az group create --name rg-houseflow --location westeurope

$SUBSCRIPTION_ID = az account show --query id -o tsv

# Créer le rôle custom (Container Apps, PostgreSQL, Logs, Storage, Network, Identity)
$roleDefinition = @"
{
  "Name": "HouseFlow Deployer",
  "Description": "Deploy Container Apps + PostgreSQL + VNet only - no VMs, no reserved instances",
  "Actions": [
    "Microsoft.App/*",
    "Microsoft.DBforPostgreSQL/flexibleServers/*",
    "Microsoft.OperationalInsights/workspaces/*",
    "Microsoft.Storage/storageAccounts/read",
    "Microsoft.Storage/storageAccounts/listKeys/action",
    "Microsoft.Storage/storageAccounts/blobServices/containers/*",
    "Microsoft.Resources/subscriptions/resourceGroups/read",
    "Microsoft.Resources/deployments/*",
    "Microsoft.Authorization/locks/*",
    "Microsoft.ManagedIdentity/userAssignedIdentities/*",
    "Microsoft.Network/virtualNetworks/*",
    "Microsoft.Network/privateDnsZones/*",
    "Microsoft.Network/networkSecurityGroups/*"
  ],
  "NotActions": [],
  "AssignableScopes": ["/subscriptions/$SUBSCRIPTION_ID/resourceGroups/rg-houseflow"]
}
"@

$roleDefinition | Out-File -Encoding utf8 role-definition.json
az role definition create --role-definition role-definition.json
Remove-Item role-definition.json

# Assigner le rôle custom au Service Principal (PAS Contributor)
az role assignment create `
  --assignee $AZURE_CLIENT_ID `
  --role "HouseFlow Deployer" `
  --scope "/subscriptions/$SUBSCRIPTION_ID/resourceGroups/rg-houseflow"
```

> **Mise à jour d'un rôle existant** : si le rôle existe déjà, utiliser `az role definition update` :
> ```powershell
> # Récupérer le rôle actuel, modifier le JSON, puis :
> az role definition update --role-definition role-definition.json
> ```

> **Pourquoi pas Contributor ?** Un Contributor peut créer n'importe quelle ressource Azure (VMs, reserved instances, Cosmos DB...). Le rôle custom limite strictement aux types de ressources dont HouseFlow a besoin.

## 4. Azure Policies — protection anti-dérapage (niveau souscription)

Ces policies s'appliquent **au niveau de la souscription** : elles couvrent tous les Resource Groups (actuels et futurs). Même avec des credentials volées, les ressources non-autorisées sont **refusées à la création**.

> **Souscription partagée ?** Si d'autres projets existent sur la même souscription, ajoute des **exclusions** sur leurs Resource Groups lors de l'assignment (champ `--not-scopes` en CLI, ou "Exclusions" dans le portail).

### 4a. Allowlist des types de ressources

```powershell
# Seuls ces types de ressources peuvent être créés dans la souscription
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
      "Microsoft.Network/virtualNetworks",
      "Microsoft.Network/virtualNetworks/subnets",
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
> `Microsoft.Resources/resourceGroups` est ajouté pour permettre la gestion du RG lui-même.

**Via le portail** : Policy → Assignments → + Assign policy → Scope = **souscription** → cherche "Allowed resource types" → Parameters → coche les types ci-dessus.

> **Mise à jour d'une policy existante** : supprimer et recréer l'assignment :
> ```powershell
> az policy assignment delete --name "houseflow-allowed-resources" --scope "/subscriptions/$SUBSCRIPTION_ID"
> # Puis relancer la commande az policy assignment create ci-dessus
> ```

### 4b. Restriction des SKUs PostgreSQL

```powershell
# Créer la policy definition (custom)
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

# Assigner la policy au niveau souscription
az policy assignment create `
  --name "houseflow-pg-sku-restrict" `
  --display-name "HouseFlow - Bloquer PostgreSQL non-Burstable" `
  --policy "houseflow-pg-sku-restrict" `
  --scope "/subscriptions/$SUBSCRIPTION_ID"
```

> Bloque : General Purpose (GP_Gen5), Memory Optimized, et tout SKU au-delà de ~30 EUR/mois.

### 4c. Vérification des policies

```powershell
# Lister les policies assignées au niveau souscription
az policy assignment list `
  --scope "/subscriptions/$SUBSCRIPTION_ID" `
  --query "[?contains(name, 'houseflow')].{name:name, policy:displayName}" -o table

# Résultat attendu :
# Name                           Policy
# -----------------------------  -----------------------------------------
# houseflow-allowed-resources    HouseFlow - Types de ressources autorises
# houseflow-pg-sku-restrict      HouseFlow - Bloquer PostgreSQL non-Burstable
```

## 5. Storage Account pour le Terraform State

```powershell
az storage account create `
  --name sthouseflowtfstate `
  --resource-group rg-houseflow `
  --sku Standard_LRS `
  --location westeurope

az storage container create `
  --name tfstate `
  --account-name sthouseflowtfstate
```

## 6. PAT GitHub (pull GHCR depuis Azure)

1. Aller sur https://github.com/settings/tokens/new (Classic token — les Fine-grained tokens ne supportent pas `packages`)
2. Créer un token avec :
   - **Note** : `houseflow-azure-ghcr-pull`
   - **Expiration** : Custom → 1 an
   - **Scopes** : cocher uniquement **`read:packages`**
3. **Generate token** → copier le token

## 7. Récupérer votre Object ID (pour l'accès DB via Entra ID)

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

## 8. Secrets GitHub Actions

Ajouter dans **Settings > Secrets and variables > Actions** du repo :

| Secret                    | Valeur                                       |
|---------------------------|----------------------------------------------|
| `AZURE_CLIENT_ID`         | Application (client) ID de l'App Registration |
| `AZURE_TENANT_ID`         | Directory (tenant) ID                         |
| `AZURE_SUBSCRIPTION_ID`   | `az account show --query id -o tsv`           |
| `GHCR_PAT`               | Le PAT Classic créé à l'étape 6               |
| `JWT_KEY`                | Clé JWT (minimum 32 caractères)               |
| `ENTRA_ADMIN_OBJECT_ID`  | `$ENTRA_OBJECT_ID` de l'étape 7               |
| `ENTRA_ADMIN_NAME`       | `$ENTRA_NAME` de l'étape 7                    |
| `BASTION_SSH_PUBLIC_KEY` | Contenu de `~/.ssh/id_ed25519.pub` (ou `id_rsa.pub`) |

## 9. Vérification finale

```powershell
$SUBSCRIPTION_ID = az account show --query id -o tsv

# App Registration
az ad app list --display-name "houseflow-github-actions" `
  --query "[].{id:appId, name:displayName}" -o table

# Federated Credentials
az ad app federated-credential list --id $AZURE_CLIENT_ID -o table

# Resource Group
az group show --name rg-houseflow `
  --query "{name:name, location:location}" -o table

# Rôle custom
az role definition list --name "HouseFlow Deployer" `
  --query "[].{name:roleName, actions:permissions[0].actions}" -o table

# Policies
az policy assignment list `
  --scope "/subscriptions/$SUBSCRIPTION_ID" `
  --query "[?contains(name, 'houseflow')].{name:name, policy:displayName}" -o table

# Storage Account
az storage account show --name sthouseflowtfstate `
  --query "{name:name, sku:sku.name}" -o table

# Entra admin info
az ad signed-in-user show --query "{objectId:id, name:userPrincipalName}" -o table
```

## 10. Se connecter à PostgreSQL (debug via DBeaver / psql)

La DB est dans un VNet privé (pas d'accès public). Un **Container App bastion** (scale-to-zero) fait office de tunnel SSH.

### Prérequis

- Clé SSH configurée (la clé publique doit être dans le secret `BASTION_SSH_PUBLIC_KEY`)
- Le bastion scale à zéro — la première connexion prend ~30s (cold start)

### Via SSH tunnel (ligne de commande)

```powershell
# 1. Obtenir un token Entra pour PostgreSQL
$token = az account get-access-token `
  --resource-type oss-rdbms `
  --query accessToken -o tsv

# 2. Ouvrir le tunnel SSH (port local 5432 → PostgreSQL privé)
# Remplacer <bastion_fqdn> par le FQDN du bastion (visible dans terraform output)
ssh -N -L 5432:psql-houseflow.houseflow.private.postgres.database.azure.com:5432 `
  bastion@<bastion_fqdn> -p 2222

# 3. Dans un autre terminal : se connecter
$env:PGPASSWORD = $token
psql "host=localhost port=5432 dbname=houseflow_preprod user=<votre-email> sslmode=require"
```

### Via DBeaver

1. **Onglet SSH** de la connexion :
   - Host : `<bastion_fqdn>` (output Terraform `bastion_fqdn`)
   - Port : `2222`
   - User : `bastion`
   - Authentication : Public Key → sélectionner votre clé privée (`~/.ssh/id_ed25519`)

2. **Onglet Main** :
   - Host : `psql-houseflow.houseflow.private.postgres.database.azure.com`
   - Port : `5432`
   - Database : `houseflow_preprod` (ou `houseflow_prod`)
   - Username : votre email Microsoft (ex: `user@domain.com`)
   - Password : le token obtenu via `az account get-access-token --resource-type oss-rdbms --query accessToken -o tsv`

> **Note** : le token Entra expire après ~1h. Regénérez-le si la connexion échoue.

## Récapitulatif des protections

```
Couche 1 — GitHub           Branch protection + required review
Couche 2 — GitHub Actions   terraform plan visible avant apply
Couche 3 — Azure RBAC       Rôle custom (pas Contributor)
Couche 4 — Azure Policy     Allowlist de ressources + SKU PostgreSQL
Couche 5 — Budget           Alerte + kill switch à 25 EUR/mois
Couche 6 — Réseau           VNet privé, PostgreSQL sans accès public
Couche 7 — Auth             Entra ID (passwordless), pas de secrets DB
```

## Migration depuis la configuration publique existante

> **Important** : Si vous avez déjà un PostgreSQL Flexible Server en mode public, il doit être **recréé** pour passer en VNet (Azure ne permet pas la migration à chaud).

Étapes manuelles avant de relancer le déploiement :

```powershell
# 1. Mettre à jour le rôle RBAC (ajouter Microsoft.Network/*)
# Récupérer le rôle existant
az role definition list --name "HouseFlow Deployer" -o json > role-definition.json
# Éditer le fichier pour ajouter les permissions Network (voir section 3)
az role definition update --role-definition role-definition.json
Remove-Item role-definition.json

# 2. Mettre à jour la policy (ajouter les types Network)
# Supprimer l'ancienne assignment
az policy assignment delete `
  --name "houseflow-allowed-resources" `
  --scope "/subscriptions/$SUBSCRIPTION_ID"
# Recréer avec les nouveaux types (voir section 4a)

# 3. Enregistrer le provider Network
az provider register --namespace Microsoft.Network

# 4. Ajouter les nouveaux secrets GitHub
# ENTRA_ADMIN_OBJECT_ID et ENTRA_ADMIN_NAME (voir sections 7 et 8)

# 5. Supprimer manuellement l'ancien PostgreSQL + Container Apps Environment
# (Terraform les recréera dans le VNet)
az postgres flexible-server delete --name psql-houseflow --resource-group rg-houseflow --yes
az containerapp env delete --name cae-houseflow --resource-group rg-houseflow --yes

# 6. Supprimer le management lock (si existant, sinon la suppression échouera)
az lock delete --name "rg-houseflow-lock" --resource-group rg-houseflow
```

Une fois toutes les cases cochées, le Terraform et les workflows GitHub Actions peuvent être déployés.
