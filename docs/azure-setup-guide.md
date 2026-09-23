# Bootstrap Azure de HouseFlow

Ce que Terraform ne peut pas créer lui-même, parce qu'il en dépend pour tourner : les
souscriptions, les resource groups qui portent les states, les rôles et les identités de
déploiement. Tout le reste est du `terraform apply`.

Cible : [`specs/infrastructure.md`](../specs/infrastructure.md). En cas de divergence, c'est elle
qui décrit l'intention, et les fichiers `infrastructure/terraform/**` qui décrivent le réel — ce
guide n'est que la procédure pour passer d'une souscription vide à un premier run vert.

> Commandes en **PowerShell** (Windows, macOS et Linux).

## Ce que ce bootstrap doit produire

Deux souscriptions dans le **même tenant** Entra : l'une porte la production, l'autre les
environnements de pull request. La frontière est une souscription et non un resource group parce
qu'un environnement jetable crée et détruit ses propres resource groups : le service principal qui
en a le droit l'a forcément à l'échelle de la souscription, et aucun tag mal posé ni bug de filtre
ne peut lui faire franchir cette limite.

Il n'y a que ces deux sortes d'environnement. Un environnement de PR étant complet — son réseau,
son serveur PostgreSQL, son Container Apps Environment, produits par le même `terraform apply` que
la production — il éprouve un changement d'infrastructure dans la PR qui l'introduit ; un
environnement de validation permanent ne prouverait rien de plus et se facturerait en continu.

Dans chacune :

| Ressource | Pourquoi elle est créée à la main |
|---|---|
| `rg-houseflow-shared` | contient le backend ; il doit exister avant le premier `terraform init` |
| un storage account de states + ses conteneurs | même raison — un backend ne peut pas se créer lui-même |
| `id-houseflow-cert`, `id-houseflow-dumps-reader` | dans la souscription jetable, aucun workflow n'applique la racine `shared` |
| rôles custom + app registrations | attribuer un rôle demande des droits qu'aucune identité de déploiement ne possède |

Une asymétrie à connaître avant de commencer : **la racine Terraform `shared` n'est appliquée que
dans la souscription de production** (job `apply-shared` de `pipeline.yml`, environnement GitHub
`prod`). Côté jetable, personne ne l'applique : `rg-houseflow-shared`, le storage, son conteneur
`tfstate`, `id-houseflow-cert` et `id-houseflow-dumps-reader` y sont intégralement posés par ce guide, et
rien d'autre n'y est attendu.

## Prérequis

- [ ] Deux souscriptions Azure actives, dans le même tenant
- [ ] Azure CLI (`winget install Microsoft.AzureCLI` ou [instructions](https://learn.microsoft.com/en-us/cli/azure/install-azure-cli)), `pwsh`, et GitHub CLI authentifié (`gh auth login`)
- [ ] `az login`
- [ ] Se placer à la racine du dépôt — les chemins `infrastructure/rbac/` ci-dessous sont relatifs

```powershell
$GITHUB_REPO   = "BarbeRouss/HouseFlow"
$LOCATION      = "westeurope"
$TENANT_ID     = az account show --query tenantId -o tsv

# À renseigner : les deux souscriptions.
$SUB_PROD      = "<id-souscription-production>"
$SUB_EPHEMERAL = "<id-souscription-jetable>"
```

### Resource providers

À enregistrer **dans chaque souscription** : c'est une action de niveau souscription, que les
rôles custom n'accordent pas — un provider oublié se manifeste bien plus tard par un
`MissingSubscriptionRegistration` en plein apply.

```powershell
foreach ($sub in $SUB_PROD, $SUB_EPHEMERAL) {
  az account set --subscription $sub
  foreach ($ns in "Microsoft.App", "Microsoft.DBforPostgreSQL", "Microsoft.OperationalInsights",
                  "Microsoft.Storage", "Microsoft.ManagedIdentity", "Microsoft.Network",
                  "Microsoft.KeyVault", "Microsoft.Web") {
    az provider register --namespace $ns
  }
}
```

`Microsoft.KeyVault` et `Microsoft.Web` ne sont pas pré-enregistrés par défaut sur une
souscription neuve.

## 1. Resource group partagé et storage des states

Le nom d'un storage account est unique au niveau mondial : les deux souscriptions ne peuvent pas
porter le même. Il n'est pas dans le code Terraform, dont les deux racines servent les deux
souscriptions, mais **en constante dans chaque workflow** — `pipeline.yml` connaît celui de la
production, `pr-preview.yml` et `reaper.yml` celui des environnements jetables — et passe de là en
`-backend-config`.

En constante plutôt qu'en secret, pour deux raisons. Ce n'est pas un identifiant d'accès : le
compte refuse l'accès anonyme et son plan de données exige AAD plus un rôle sur le conteneur, si
bien que connaître son nom ne donne rien. Et un secret absent devient une chaîne vide sans
avertissement, ce qui faisait échouer `terraform init` sur un « `accountName` cannot be an empty
string » qui ne nommait pas le secret manquant.

Les noms ci-dessous sont ceux que les workflows attendent : en changer suppose de les y changer
aussi.

```powershell
# Ces deux noms sont en constante dans les workflows (`TFSTATE_ACCOUNT`) : les
# changer ici suppose de les y changer aussi.
$ST_PROD      = "sthouseflowtfstateprod"
$ST_EPHEMERAL = "sthouseflowtfstateeph"

az account set --subscription $SUB_PROD
az group create --name rg-houseflow-shared --location $LOCATION
az storage account create --name $ST_PROD --resource-group rg-houseflow-shared `
  --sku Standard_LRS --location $LOCATION `
  --allow-blob-public-access false --min-tls-version TLS1_2
az storage container create --name tfstate        --account-name $ST_PROD --auth-mode login

az account set --subscription $SUB_EPHEMERAL
az group create --name rg-houseflow-shared --location $LOCATION
az storage account create --name $ST_EPHEMERAL --resource-group rg-houseflow-shared `
  --sku Standard_LRS --location $LOCATION `
  --allow-blob-public-access false --min-tls-version TLS1_2
az storage container create --name tfstate --account-name $ST_EPHEMERAL --auth-mode login
```

Un conteneur de chaque côté, `tfstate`, et une clé par instance :

| Souscription | Clés |
|---|---|
| production | `shared.tfstate`, `environment-prod.tfstate` |
| jetable | `environment-pr-<n>.tfstate` |

`shared.tfstate` n'existe que du côté production : c'est la seule souscription où la racine
`shared` est appliquée.

## 2. Rôles custom

Deux définitions versionnées dans `infrastructure/rbac/`, qui font foi — le tableau ci-dessous
n'en est qu'un résumé. Le détail du contenu et des arbitrages :
[`infrastructure/rbac/README.md`](../infrastructure/rbac/README.md).

| Rôle | Scope assignable | Ce qu'il permet |
|---|---|---|
| `HouseFlow Deployer` | souscription | tout le plan de gestion d'un environnement, resource group compris, sans aucun droit d'attribution de rôle |
| `HouseFlow Deployer (subscription)` | souscription | `Microsoft.Web/locations/*/read`, en lecture seule |

`HouseFlow Deployer` est assignable à la souscription, et non à un resource group, parce qu'un
environnement jetable crée le sien : son nom n'est pas connu à l'avance, donc aucune assignation
plus étroite n'est possible. C'est ce qui lui vaut `resourceGroups/write` et `/delete`.

> **`HouseFlow Shared Tenant` a été supprimé.** Il ne portait plus que
> `Microsoft.KeyVault/vaults/read` sur `rg-houseflow-shared`, or la racine `environment` ne lit
> plus le Key Vault par data source — l'URI du coffre est dérivée de son nom
> (`TF_VAR_key_vault_name`), précisément pour qu'un environnement jetable n'ait aucun droit de
> plan de gestion sur le coffre de production. Si une assignation résiduelle existe dans le
> tenant, elle peut être retirée sans conséquence.

Une définition de rôle custom est unique par **annuaire**, pas par souscription : son nom ne peut
exister qu'une fois dans le tenant, et c'est `assignableScopes` qui décide où elle peut être
assignée. Elle se crée donc **une seule fois**, en listant les deux souscriptions — tenter de la
créer une seconde fois échoue en `RoleDefinitionWithSameNameExists`.

C'est aussi pourquoi les deux souscriptions doivent être dans le même tenant : sans cela, il
faudrait deux définitions, deux jeux d'app registrations, et le partage du certificat décrit au §5
serait impossible.

```powershell
function New-HouseFlowRole($File) {
  # Le JSON porte un placeholder par souscription : le lire suffit à voir que le rôle
  # en couvre deux.
  $def = (Get-Content $File -Raw) `
    -replace "<SUBSCRIPTION_ID_PROD>", $SUB_PROD `
    -replace "<SUBSCRIPTION_ID_EPHEMERAL>", $SUB_EPHEMERAL
  $tmp = New-TemporaryFile
  Set-Content -Path $tmp -Value $def -NoNewline

  if (az role definition list --name (($def | ConvertFrom-Json).roleName) --custom-role-only true --query "[0].roleName" -o tsv) {
    az role definition update --role-definition "@$tmp" -o none
  } else {
    az role definition create --role-definition "@$tmp" -o none
  }
  Remove-Item $tmp
}

az account set --subscription $SUB_PROD
New-HouseFlowRole "infrastructure/rbac/houseflow-deployer.role.json"
```

Un `assignableScopes` élargi met parfois une minute à se propager : si l'assignation du §4 échoue
en `RoleDefinitionDoesNotExist`, attendre et relancer.

**Vérifier tout de suite que le rôle porte son nom.** `az role definition create` lit le nom
d'affichage dans le champ `name`, qui vaut le GUID de la définition dans un JSON exporté depuis
Azure : un rôle créé à partir d'un tel export apparaît dans le portail sous son GUID.

```powershell
az role definition list --custom-role-only true --query "[?starts_with(roleName,'HouseFlow')].roleName" -o tsv
```

Si un GUID sort de là, corrige le nom **sans supprimer le rôle** — le supprimer orphelinerait ses
assignations : portail → Abonnements → *la souscription* → Contrôle d'accès (IAM) → onglet
**Rôles** → le rôle → **Modifier**.

> **Mettre à jour un rôle existant** : modifier le JSON versionné, puis `az role definition
> update`. La mise à jour identifie le rôle par son GUID, qu'il faut injecter dans `name` :
> ```powershell
> $def = (Get-Content infrastructure/rbac/houseflow-deployer.role.json -Raw) `
>   -replace '<SUBSCRIPTION_ID_PROD>', $SUB_PROD `
>   -replace '<SUBSCRIPTION_ID_EPHEMERAL>', $SUB_EPHEMERAL | ConvertFrom-Json
> $def.name = az role definition list --custom-role-only true --query "[?roleName=='$($def.roleName)'].name | [0]" -o tsv
> $def | ConvertTo-Json -Depth 10 | Out-File -Encoding utf8 role-definition.json
> az role definition update --role-definition role-definition.json
> Remove-Item role-definition.json
> ```
> Une seule mise à jour suffit : le rôle est unique dans l'annuaire. Un type de ressource
> nouvellement utilisé se manifeste à l'apply par `AuthorizationFailed` — ajouter l'action au JSON
> et rejouer ce bloc.

> **Pourquoi pas Contributor ?** Un Contributor peut créer n'importe quoi — VMs, instances
> réservées, Cosmos DB. Le rôle custom borne la casse à ce que HouseFlow déploie réellement, et
> surtout il n'accorde pas `roleAssignments/write` : une identité de déploiement ne peut pas
> s'élargir elle-même.

> **Pourquoi `Microsoft.Web/locations/*/read` et pas l'action exacte ?** Le contrôle
> d'autorisation réclame `Microsoft.Web/locations/staticSitesOperationStatuses/read`, mais cette
> action n'est pas publiée dans le registre d'opérations du provider :
> `az role definition create` la refuse (`InvalidActionOrNotAction`). Le wildcard passe la
> validation et couvre l'action à l'évaluation.

## 3. App registrations et federated credentials

Une app registration par environnement GitHub qui accède à Azure : `prod` et `preview`.
`prod-approval` n'en a aucune — c'est une gate d'approbation humaine, elle n'accède à rien.

- **App registration** : la définition de l'identité dans Entra ID.
- **Service principal** : l'objet local au tenant qui reçoit les rôles. Une app registration sans
  service principal ne peut se voir assigner aucun droit.
- **Federated credential** : une *confiance*, pas un droit. Elle dit qui peut demander un token
  OIDC au nom de cette identité ; ce que ce token permet ensuite relève du RBAC (§4).

```powershell
$APP = @{}
foreach ($e in "preview", "prod") {
  $APP[$e] = az ad app create --display-name "houseflow-github-$e" --query appId -o tsv
  az ad sp create --id $APP[$e]

  $params = @{
    name      = "github-actions-$e"
    issuer    = "https://token.actions.githubusercontent.com"
    subject   = "repo:$GITHUB_REPO" + ":environment:$e"
    audiences = @("api://AzureADTokenExchange")
  } | ConvertTo-Json -Depth 5
  $tmp = New-TemporaryFile
  Set-Content -Path $tmp -Value $params -NoNewline
  az ad app federated-credential create --id $APP[$e] --parameters "@$tmp"
  Remove-Item $tmp
}
```

Le subject porte le nom de l'**environnement**, jamais la branche : le token OIDC délivré par
GitHub est identifié par l'environnement du job.

> **La casse du subject est significative.** `BarbeRouss/HouseFlow` et `barberouss/houseflow` ne
> sont pas le même subject : le second échoue en `AADSTS7002138 — The subject matches with
> case-insensitive comparison, but not with case-sensitive comparison`. Utilise la casse exacte
> du dépôt telle que GitHub la stocke.
>
> ```powershell
> foreach ($e in "preview","prod") {
>   "$e : " + (az ad app federated-credential list --id $APP[$e] --query "[].subject" -o tsv)
> }
> ```

> **Via le portail** : Entra ID → App registrations → l'app → Certificates & secrets → Federated
> credentials → + Add credential → GitHub Actions deploying Azure resources → Entity type
> **Environment**.

## 4. Attributions de rôles

La ligne de partage : `sp-prod` n'a **aucun** rôle dans la souscription jetable, et `sp-preview`
n'en a aucun dans celle de production. Les seules exceptions sont les deux droits de lecture
data-plane accordés au §5, qui vont dans le sens jetable → production : un secret unique, et le
conteneur `db-dumps`.

```powershell
# ── Souscription de production ───────────────────────
az account set --subscription $SUB_PROD
$scopeProd = "/subscriptions/$SUB_PROD"

az role assignment create --assignee $APP["prod"] --role "HouseFlow Deployer" --scope $scopeProd

# Émission du certificat : lego écrit dans kv-houseflow. Rôles data-plane posés sur le resource
# group, hérités par le coffre quand la racine `shared` le créera — il n'existe pas encore.
az role assignment create --assignee $APP["prod"] --role "Key Vault Certificates Officer" `
  --scope "$scopeProd/resourceGroups/rg-houseflow-shared"
az role assignment create --assignee $APP["prod"] --role "Key Vault Secrets Officer" `
  --scope "$scopeProd/resourceGroups/rg-houseflow-shared"

$stProdScope = "$scopeProd/resourceGroups/rg-houseflow-shared/providers/Microsoft.Storage/storageAccounts/$ST_PROD/blobServices/default/containers"
az role assignment create --assignee $APP["prod"] --role "Storage Blob Data Contributor" --scope "$stProdScope/tfstate"

# ── Souscription jetable ─────────────────────────────
az account set --subscription $SUB_EPHEMERAL
$scopeEph = "/subscriptions/$SUB_EPHEMERAL"

az role assignment create --assignee $APP["preview"] --role "HouseFlow Deployer" --scope $scopeEph

$stEphScope = "$scopeEph/resourceGroups/rg-houseflow-shared/providers/Microsoft.Storage/storageAccounts/$ST_EPHEMERAL/blobServices/default/containers"
az role assignment create --assignee $APP["preview"] --role "Storage Blob Data Contributor" --scope "$stEphScope/tfstate"
```

`sp-preview` est seul dans la souscription jetable, et c'est lui qui porte aussi `reaper.yml` :
le droit de supprimer un resource group n'a de valeur que là, jamais en production.

`Storage Blob Data Contributor` est posé **par conteneur** et jamais sur le compte : les backends
Terraform s'authentifient en AAD sur le plan de données (`use_azuread_auth`), et le nettoyage du
state par `pr-preview.yml` et par `reaper.yml` passe par `az storage blob delete --auth-mode
login`. Aucun de ces chemins ne demande les clés du compte.

Le rôle de souscription pour les Static Web Apps est créé et assigné par un script idempotent,
dans chaque souscription, aux identités qui y déploient :

```powershell
# -AssignableScopeSubscriptionIds porte les DEUX souscriptions à chaque appel : le rôle
# est unique dans l'annuaire, et le passer partiellement retirerait l'autre scope.
pwsh infrastructure/rbac/Assign-DeployerSubscriptionRole.ps1 `
  -SubscriptionId $SUB_PROD `
  -AssignableScopeSubscriptionIds $SUB_PROD,$SUB_EPHEMERAL `
  -SpDisplayName houseflow-github-prod

pwsh infrastructure/rbac/Assign-DeployerSubscriptionRole.ps1 `
  -SubscriptionId $SUB_EPHEMERAL `
  -AssignableScopeSubscriptionIds $SUB_PROD,$SUB_EPHEMERAL `
  -SpDisplayName houseflow-github-preview
```

Les deux environnements en ont besoin : le frontend est une Static Web App partout, production
comprise, et lier son domaine custom est une opération longue dont Azure publie l'état hors
resource group. Passer `-SpDisplayName` explicitement vise une identité à la fois ; sans ce paramètre, le script
retombe sur ses deux valeurs par défaut (`houseflow-github-preview` et `houseflow-github-prod`) et
tenterait d'assigner le rôle à une identité absente de la souscription visée.

### 4a. `sp-prod` — RBAC Administrator conditionné (ABAC)

La racine `shared` crée **deux** attributions de rôle : `Key Vault Secrets User` pour
`id-houseflow-cert`, sur le seul secret du certificat, et `Storage Blob Data Contributor` pour
`id-houseflow-dumps-writer`, sur le seul conteneur `db-dumps` (le job qui y publie le dump pseudonymisé
de la nuit). `HouseFlow Deployer` n'accorde délibérément pas `roleAssignments/write` — sans quoi
toute identité de déploiement pourrait s'élargir elle-même. `sp-prod` reçoit donc ce droit
séparément, borné par une condition ABAC aux deux seuls rôles qu'il a besoin de distribuer.

```powershell
az account set --subscription $SUB_PROD

$kvSecretsUser     = "4633458b-17de-408a-b874-0445c86b69e6"   # Key Vault Secrets User
$blobContributor   = "ba92f5b4-2d11-453d-a403-e96b0029c9fe"   # Storage Blob Data Contributor
$distributable     = "$kvSecretsUser, $blobContributor"

$condition = "((!(ActionMatches{'Microsoft.Authorization/roleAssignments/write'})) OR " +
  "(@Request[Microsoft.Authorization/roleAssignments:RoleDefinitionId] ForAnyOfAnyValues:GuidEquals {$distributable}))" +
  " AND " +
  "((!(ActionMatches{'Microsoft.Authorization/roleAssignments/delete'})) OR " +
  "(@Resource[Microsoft.Authorization/roleAssignments:RoleDefinitionId] ForAnyOfAnyValues:GuidEquals {$distributable}))"

az role assignment create `
  --assignee $APP["prod"] `
  --role "Role Based Access Control Administrator" `
  --scope "/subscriptions/$SUB_PROD/resourceGroups/rg-houseflow-shared" `
  --condition $condition `
  --condition-version "2.0"
```

**Installation existante** (condition posée avant l'ajout de `db-dumps`, au seul
`Key Vault Secrets User`) : sans cette mise à jour, `apply-shared` échoue en `AuthorizationFailed`
sur `azurerm_role_assignment.dumps_writer`. Remplacer l'attribution — une condition ne se modifie
pas en place avec `az` :

```powershell
az role assignment delete --assignee $APP["prod"] --role "Role Based Access Control Administrator" `
  --scope "/subscriptions/$SUB_PROD/resourceGroups/rg-houseflow-shared"
# puis la commande `az role assignment create` ci-dessus, avec la nouvelle condition
```

`Storage Blob Data Contributor` est ainsi distribuable au scope de `rg-houseflow-shared`, donc
aussi sur le conteneur `tfstate`. C'est un droit que `sp-prod` possède déjà lui-même, sur ce
même conteneur : la condition ne lui ouvre rien qu'il ne puisse déjà faire.

## 5. `id-houseflow-cert` et le certificat inter-souscriptions

Le certificat wildcard est le seul lien entre les deux souscriptions, et il est irréductible :
Let's Encrypt plafonne à **5 certificats identiques par semaine**, donc un environnement jetable
ne peut pas émettre le sien. Il emprunte celui de la production, en lecture seule.

Chaque Container Apps Environment attache `id-houseflow-cert` — l'identité de sa souscription —
pour résoudre sa référence Key Vault. C'est cette indirection qui rend un environnement jetable
créable **sans droit d'attribution de rôle** : si l'identité de l'environnement devait lire le
coffre, il faudrait lui poser un rôle à chaque création, donc confier au service principal de
déploiement le pouvoir d'en distribuer.

Côté production, l'identité et son attribution sont créées par la racine `shared`. Côté jetable,
elles sont posées ici, une fois pour toutes :

```powershell
az account set --subscription $SUB_EPHEMERAL
$certIdentityPrincipal = az identity create --name id-houseflow-cert `
  --resource-group rg-houseflow-shared --location $LOCATION --query principalId -o tsv
```

L'attribution se fait ensuite **dans la souscription de production**, au scope du secret seul —
pas du coffre, pas du resource group. Elle suppose que `kv-houseflow` et son certificat existent
déjà : cette commande vient donc après le premier run de `pipeline.yml` (§9).

```powershell
az account set --subscription $SUB_PROD
az role assignment create `
  --assignee-object-id $certIdentityPrincipal --assignee-principal-type ServicePrincipal `
  --role "Key Vault Secrets User" `
  --scope "/subscriptions/$SUB_PROD/resourceGroups/rg-houseflow-shared/providers/Microsoft.KeyVault/vaults/kv-houseflow/secrets/wildcard-houseflow-cloud"
```

### 5a. `id-houseflow-dumps-reader` — les données de prod pseudonymisées

Même indirection, pour la même raison : le job `dbtools restore` d'un environnement de PR attache
`id-houseflow-dumps-reader` pour télécharger le dump pseudonymisé que la production publie chaque
nuit dans `db-dumps`. Son homologue de production, `id-houseflow-dumps-writer`, est créée par la
racine `shared` avec `Storage Blob Data Contributor` (§4a). Les noms disent les droits, pour qu'on
ne confonde pas les deux souscriptions. Celle-ci est posée ici, **en lecture seule**, sur ce seul
conteneur :

```powershell
az account set --subscription $SUB_EPHEMERAL
$dumpsIdentityPrincipal = az identity create --name id-houseflow-dumps-reader `
  --resource-group rg-houseflow-shared --location $LOCATION --query principalId -o tsv

az account set --subscription $SUB_PROD
az role assignment create `
  --assignee-object-id $dumpsIdentityPrincipal --assignee-principal-type ServicePrincipal `
  --role "Storage Blob Data Reader" `
  --scope "/subscriptions/$SUB_PROD/resourceGroups/rg-houseflow-shared/providers/Microsoft.Storage/storageAccounts/$ST_PROD/blobServices/default/containers/db-dumps"
```

Le conteneur est créé par le premier `apply-shared` : comme pour le certificat, cette attribution
vient après le premier run de `pipeline.yml` (§9). Sans elle, le job de restauration d'une PR
échoue en HTTP 403 et fait échouer son déploiement.

Le sens de la dépendance compte : le jetable lit le permanent, jamais l'inverse. Aucune identité
de la souscription de production n'a quoi que ce soit dans l'autre, et aucune identité jetable
ne peut écrire dans `db-dumps` — une PR ne peut donc pas substituer le dump que les autres
restaureront.

## 6. Azure Policy — garde-fou anti-dérapage

Assignées **au niveau de chaque souscription**, donc couvrant les resource groups créés plus tard
par les environnements jetables. Même avec des credentials volées, une ressource hors liste est
refusée à la création.

> **Souscription partagée avec d'autres projets ?** Ajouter des exclusions sur leurs resource
> groups (`--not-scopes`).

### 6a. Allowlist des types de ressources

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
      "Microsoft.App/jobs",
      "Microsoft.Web/staticSites",
      "Microsoft.Web/staticSites/customDomains",
      "Microsoft.DBforPostgreSQL/flexibleServers",
      "Microsoft.DBforPostgreSQL/flexibleServers/databases",
      "Microsoft.DBforPostgreSQL/flexibleServers/administrators",
      "Microsoft.DBforPostgreSQL/flexibleServers/configurations",
      "Microsoft.Storage/storageAccounts",
      "Microsoft.OperationalInsights/workspaces",
      "Microsoft.Authorization/locks",
      "Microsoft.ManagedIdentity/userAssignedIdentities",
      "Microsoft.Resources/resourceGroups",
      "Microsoft.KeyVault/vaults",
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

foreach ($sub in $SUB_PROD, $SUB_EPHEMERAL) {
  az policy assignment create `
    --name "houseflow-allowed-resources" `
    --display-name "HouseFlow - Types de ressources autorises" `
    --policy "a08ec900-254a-4555-9bf5-e42af04b5c5c" `
    --scope "/subscriptions/$sub" `
    --params allowed-resources-params.json
}

Remove-Item allowed-resources-params.json
```

`Microsoft.App/jobs` porte les jobs `dbtools` (dump nocturne en prod, restauration dans chaque
PR). `Microsoft.Resources/resourceGroups` est indispensable depuis que chaque environnement crée
le sien.

> **Mise à jour d'une assignation** : la supprimer et la recréer
> (`az policy assignment delete --name "houseflow-allowed-resources" --scope "/subscriptions/$sub"`).

### 6b. SKU PostgreSQL

Chaque environnement a son propre serveur : un SKU laissé à `GP_Standard_D2s` sur un gabarit de
PR se multiplierait par le nombre de PR ouvertes. C'est la policy qui borne la facture, pas la
relecture des `.tfvars`.

```powershell
$policyRules = @"
{
  "if": {
    "allOf": [
      { "field": "type", "equals": "Microsoft.DBforPostgreSQL/flexibleServers" },
      { "not": { "field": "Microsoft.DBforPostgreSQL/flexibleServers/sku.name", "in": ["Standard_B1ms", "Standard_B2s"] } }
    ]
  },
  "then": { "effect": "deny" }
}
"@

$policyRules | Out-File -Encoding utf8 pg-sku-rules.json

foreach ($sub in $SUB_PROD, $SUB_EPHEMERAL) {
  az policy definition create `
    --name "houseflow-pg-sku-restrict" `
    --display-name "HouseFlow - PostgreSQL SKU Burstable uniquement" `
    --mode "All" --rules pg-sku-rules.json --subscription $sub

  az policy assignment create `
    --name "houseflow-pg-sku-restrict" `
    --display-name "HouseFlow - Bloquer PostgreSQL non-Burstable" `
    --policy "houseflow-pg-sku-restrict" `
    --scope "/subscriptions/$sub"
}

Remove-Item pg-sku-rules.json
```

### 6c. Quota de vCores de la souscription jetable

Le plafond de previews simultanées (`MAX_PR_ENVS` dans `pr-preview.yml`) n'est pas un choix de
coût mais un ordre de grandeur du quota : chaque PR crée un Flexible Server. Vérifier ce que la
souscription autorise avant de relever ce plafond.

## 7. Credentials externes

### 7a. PAT GitHub — pull GHCR depuis Azure

1. https://github.com/settings/tokens/new — token **Classic** : les fine-grained ne gèrent pas `packages`
2. Note `houseflow-azure-ghcr-pull`, expiration 1 an, scope **`read:packages`** uniquement
3. Copier le token — il va au secret de dépôt `GHCR_PAT`

### 7b. Administrateur Entra de PostgreSQL

Les serveurs sont en authentification Entra **exclusive**, sans mot de passe. Ces deux valeurs
font de toi l'administrateur humain de chaque serveur créé, ce qui est la seule façon d'ouvrir un
`psql` par le bastion (§10).

```powershell
$ENTRA_OBJECT_ID = az ad signed-in-user show --query id -o tsv
$ENTRA_NAME      = az ad signed-in-user show --query userPrincipalName -o tsv
```

### 7c. API OVH (DNS-01 et enregistrements d'environnement)

Le job `certificate` valide le challenge DNS-01 et chaque environnement pose ses propres
enregistrements : les mêmes credentials servent aux deux.

1. Générer un token sur https://api.ovh.com/createToken/, avec quatre droits sur le chemin
   `/domain/zone/houseflow.cloud/*` : `GET`, `POST`, `PUT`, `DELETE`
2. Valider le Consumer Key via l'URL de confirmation (connexion + 2FA)
3. Noter Application Key, Application Secret et Consumer Key

## 8. GitHub — environnements, secrets et variables

### 8a. Environnements

Trois : `prod` et `prod-approval` limités à `main`, `preview` ouvert à toutes les branches.

> **La limitation de branche est la protection principale de la production, pas un réglage
> cosmétique.** Sur un événement `pull_request`, GitHub exécute le workflow **tel qu'il est dans
> la branche de la PR**. Sans restriction, une PR qui ajoute un job `environment: prod` — dans
> `pr-preview.yml`, ou en collant un `on: pull_request` sur `pipeline.yml` — obtiendrait un token
> OIDC de subject `repo:BarbeRouss/HouseFlow:environment:prod`. La federated credential ne
> contraint que l'environnement, jamais la branche, et `prod` n'a **aucun** required reviewer par
> construction (la gate est sur `prod-approval`) : le job partirait sans approbation, avec tous
> les droits de `sp-prod`. Même chose via `gh workflow run --ref <branche>`. Avec la politique de
> branche, GitHub refuse de démarrer le job et ne délivre aucun token.
>
> `preview` reste ouvert — une preview de PR tourne forcément depuis une branche de PR — et c'est
> acceptable depuis que `sp-preview` n'a de droits que dans la souscription jetable.

**Via l'UI** : Settings → Environments → New environment.
- `prod` : Deployment branches and tags → Selected branches → `main`. Pas de required reviewer.
- `prod-approval` : Required reviewers → le mainteneur ; même restriction de branche.
- `preview` : créer, ne rien configurer.

```powershell
gh api --method PUT "repos/$GITHUB_REPO/environments/preview" | Out-Null

$branchOnlyBody = @{
  deployment_branch_policy = @{ protected_branches = $false; custom_branch_policies = $true }
} | ConvertTo-Json -Depth 5
$branchOnlyBody | Out-File -Encoding utf8 env-branch-only.json

gh api --method PUT "repos/$GITHUB_REPO/environments/prod" --input env-branch-only.json | Out-Null
gh api --method POST "repos/$GITHUB_REPO/environments/prod/deployment-branch-policies" -f name='main' | Out-Null
Remove-Item env-branch-only.json

$maintainerId = gh api users/BarbeRouss --jq ".id"
$prodApprovalBody = @{
  reviewers = @(@{ type = "User"; id = [int]$maintainerId })
  deployment_branch_policy = @{ protected_branches = $false; custom_branch_policies = $true }
} | ConvertTo-Json -Depth 5
$prodApprovalBody | Out-File -Encoding utf8 prod-approval-env.json
gh api --method PUT "repos/$GITHUB_REPO/environments/prod-approval" --input prod-approval-env.json
Remove-Item prod-approval-env.json
gh api --method POST "repos/$GITHUB_REPO/environments/prod-approval/deployment-branch-policies" -f name='main'

# Vérification : les deux environnements sensibles ne listent que `main`.
foreach ($envName in "prod", "prod-approval") {
  "$envName : " + (gh api "repos/$GITHUB_REPO/environments/$envName/deployment-branch-policies" --jq '[.branch_policies[].name] | join(", ")')
}
```

### 8b. Secrets d'environnement

Relevés dans les trois workflows qui touchent Azure : `pipeline.yml`, `pr-preview.yml` et
`reaper.yml`.

| Environnement | Secret | Valeur | Consommé par |
|---|---|---|---|
| `prod` | `AZURE_CLIENT_ID` | app `houseflow-github-prod` | `pipeline.yml` |
| `prod` | `AZURE_SUBSCRIPTION_ID` | `$SUB_PROD` | `pipeline.yml` |
| `prod` | `JWT_KEY` | clé de signature JWT, 32 caractères au moins | `pipeline.yml` |
| `prod` | `BASTION_SSH_PUBLIC_KEY` | `~/.ssh/id_ed25519.pub` | `pipeline.yml` |
| `preview` | `AZURE_CLIENT_ID` | app `houseflow-github-preview` | `pr-preview.yml`, `reaper.yml` |
| `preview` | `AZURE_SUBSCRIPTION_ID` | `$SUB_EPHEMERAL` | `pr-preview.yml`, `reaper.yml` |
| `preview` | `JWT_KEY` | clé distincte, données de démonstration | `pr-preview.yml` |

`prod-approval` ne porte aucun secret : son job est vide et n'appelle rien.

Les deux `JWT_KEY` portent le même nom parce qu'elles alimentent la même variable Terraform
(`TF_VAR_jwt_key`). **Ce n'est pas le nom qui isole les clés** mais la portée : un secret
d'environnement n'est lisible que par un job qui déclare cet environnement, et le state jetable
contient la clé en clair. Une clé de production qui s'y retrouverait serait lisible par n'importe
quelle preview.

> **`AZURE_SUBSCRIPTION_ID` est aujourd'hui un secret de _dépôt_, et c'est ce qui manque pour que
> la séparation des souscriptions soit réelle.** Tant qu'il n'est pas posé au niveau de chaque
> environnement, les trois workflows résolvent le même identifiant et déploient tout dans la même
> souscription : le cloisonnement décrit ici est alors une intention, pas un fait. Poser les deux
> secrets d'environnement ci-dessus, puis **supprimer** le secret de dépôt — tant qu'il existe, un
> environnement auquel on aurait oublié le sien y retombe silencieusement.

`preview` n'a pas de `BASTION_SSH_PUBLIC_KEY` : on n'ouvre pas de tunnel SSH vers une base qui vit
quatre heures et ne contient que des données de démonstration.

```powershell
gh secret set AZURE_CLIENT_ID --repo $GITHUB_REPO --env prod    --body $APP["prod"]
gh secret set AZURE_CLIENT_ID --repo $GITHUB_REPO --env preview --body $APP["preview"]

gh secret set AZURE_SUBSCRIPTION_ID --repo $GITHUB_REPO --env prod    --body $SUB_PROD
gh secret set AZURE_SUBSCRIPTION_ID --repo $GITHUB_REPO --env preview --body $SUB_EPHEMERAL


# Sans --body : prompt interactif, la clé ne passe pas par l'historique du shell.
gh secret set JWT_KEY --repo $GITHUB_REPO --env prod
gh secret set JWT_KEY --repo $GITHUB_REPO --env preview

gh secret set BASTION_SSH_PUBLIC_KEY --repo $GITHUB_REPO --env prod --body (Get-Content ~/.ssh/id_ed25519.pub -Raw)

# Vérification : rien de ce qui doit être cloisonné ne doit apparaître au niveau du dépôt.
gh secret list --repo $GITHUB_REPO
gh secret list --repo $GITHUB_REPO --env prod
```

### 8c. Secrets et variables de dépôt

Ceux qui sont légitimement communs aux deux souscriptions : le tenant est unique, l'administrateur
Entra est la même personne, et la zone DNS comme le registre d'images sont partagés.

| Secret (dépôt) | Valeur |
|---|---|
| `AZURE_TENANT_ID` | `$TENANT_ID` |
| `GHCR_PAT` | le PAT Classic de §7a |
| `OVH_APPLICATION_SECRET` | §7c |
| `OVH_CONSUMER_KEY` | §7c |
| `ENTRA_ADMIN_OBJECT_ID` | `$ENTRA_OBJECT_ID` |
| `ENTRA_ADMIN_NAME` | `$ENTRA_NAME` |

| Variable (dépôt) | Valeur |
|---|---|
| `OVH_APPLICATION_KEY` | §7c |
| `LETSENCRYPT_EMAIL` | contact du compte ACME (repli `admin@houseflow.cloud`) |
| `KEY_VAULT_NAME` | facultatif, `kv-houseflow` par défaut |

`KEY_VAULT_NAME` existe parce qu'un nom de coffre est unique au niveau mondial et reste réservé
sept jours après une suppression : en changer permet de repartir sans attendre une purge, qui est
une opération de niveau souscription que `HouseFlow Deployer` n'accorde pas.

Le certificat est toujours émis par la **production** de Let's Encrypt, et il n'y a pas de réglage
pour en changer : le job d'émission est idempotent — il ne réémet que si le certificat est absent,
expire dans moins de trente jours, ou provient du staging alors que la production est configurée.
Un coffre créé une fois consomme donc exactement un certificat, quel que soit le nombre de fois
que le pipeline est relancé ensuite. Le seul bouton qui court-circuite cette idempotence est
l'entrée `force_certificate` du `workflow_dispatch`, à n'utiliser qu'en connaissance de cause.

Si un jour le coffre doit être détruit et recréé plusieurs fois d'affilée, le serveur de staging
existe pour éprouver la chaîne sans consommer le quota — l'URL est en commentaire dans
`pipeline.yml`, au-dessus de `ACME_SERVER`. C'est volontairement une modification de code et non
une variable de dépôt : le garde-fou qui détecte un certificat de staging ne se déclenche que s'il
est comparé à une configuration de production, si bien qu'une variable posée puis oubliée
laisserait la prod servir indéfiniment un certificat rejeté par les navigateurs, avec un job vert.

Une seule variable pilote les trois consommateurs — le job `certificate` qui importe dans le
coffre, la racine `shared` qui le crée (`TF_VAR_key_vault_name`), et les environnements qui en
dérivent l'URI du secret. Elle désigne toujours le coffre de **production**, y compris pour les
environnements jetables : c'est la conséquence directe du certificat unique. Les environnements
en dérivent l'URI au lieu de la lire par data source, justement pour n'avoir aucun droit de plan
de gestion sur ce coffre.

```powershell
gh secret set AZURE_TENANT_ID --repo $GITHUB_REPO --body $TENANT_ID
gh secret set GHCR_PAT --repo $GITHUB_REPO
gh secret set OVH_APPLICATION_SECRET --repo $GITHUB_REPO
gh secret set OVH_CONSUMER_KEY --repo $GITHUB_REPO
gh secret set ENTRA_ADMIN_OBJECT_ID --repo $GITHUB_REPO --body $ENTRA_OBJECT_ID
gh secret set ENTRA_ADMIN_NAME --repo $GITHUB_REPO --body $ENTRA_NAME

gh variable set OVH_APPLICATION_KEY --repo $GITHUB_REPO
gh variable set LETSENCRYPT_EMAIL --repo $GITHUB_REPO --body "admin@houseflow.cloud"
gh variable set KEY_VAULT_NAME --repo $GITHUB_REPO --body $KEY_VAULT
```

## 9. Premier run

```powershell
gh workflow run pipeline.yml --repo $GITHUB_REPO --ref main -f force_infra=true
```

L'enchaînement : `build` → `apply-shared` (Key Vault, `id-houseflow-cert`, conteneur `db-dumps`)
→ `certificate` (émission Let's Encrypt et import dans le coffre) → `plan-prod` → **approbation**
→ `apply-prod`.

L'approbation arrive **après** le plan, et c'est l'essentiel du dispositif : un environnement de
PR est toujours créé depuis zéro, donc il prouve que le code produit une infrastructure qui
fonctionne, jamais que ce même code appliqué à l'état existant de la production est inoffensif.
Un `replace` du serveur PostgreSQL n'apparaît que dans un plan contre la prod. Le job `apply-prod`
consomme le fichier de plan approuvé : si l'état a bougé entre-temps, Terraform refuse plutôt que
d'appliquer autre chose que ce qui a été lu.

Le plan est publié dans le résumé du run (tronqué à 900 Ko) et archivé en entier dans l'artefact
`plan-prod`. C'est ce qu'il faut lire avant d'approuver depuis l'onglet **Actions**.

Une fois `kv-houseflow`, son certificat et le conteneur `db-dumps` créés, revenir poser les deux
attributions inter-souscriptions de §5 et §5a — elles référencent des ressources qui n'existaient
pas avant ce run.

Le premier dump pseudonymisé tourne la nuit suivante (02:00 UTC). Pour ne pas l'attendre :

```powershell
az account set --subscription $SUB_PROD
az containerapp job start -n job-dbtools-dump -g rg-houseflow-prod
```

Avant ce premier dump, un environnement de PR démarre sur ses seules données de démo — le job de
restauration le signale et réussit. Procédure et logs : [`dbtools/README.md`](../dbtools/README.md).

Puis, pour vérifier le chemin jetable de bout en bout : **ouvrir une pull request**. C'est le seul
moyen de créer un environnement jetable, et c'est voulu — il n'existe plus de création à la
demande, donc plus d'environnement qu'on puisse oublier d'avoir lancé.

```powershell
gh workflow run reaper.yml --repo $GITHUB_REPO --ref main -f dry_run=true
```

Le reaper en simulation doit lister `rg-houseflow-pr-<n>` comme conservé avec son échéance, et ne
**jamais** montrer `rg-houseflow-prod` : la production ne porte pas de tag `ttl`, donc le filtre
`az group list` ne la transmet même pas au runner. C'est toute sa protection, et elle suffit —
aucune liste d'exclusion à tenir à jour, aucun nom en dur à oublier.

Pour ré-émettre le certificat sans attendre le cron mensuel :

```powershell
gh workflow run pipeline.yml --repo $GITHUB_REPO --ref main -f force_certificate=true
```

## 10. Se connecter à PostgreSQL

Les serveurs sont en accès privé, sans adresse publique. Un Container App bastion scale-to-zero
sert de tunnel SSH ; il n'existe que sur les instances qui l'activent (`bastion_enabled`), soit la
seule production. Un environnement de PR n'en a pas : on n'ouvre pas de tunnel vers une base qui
vit quatre heures et ne contient que des données pseudonymisées. La première connexion prend une
trentaine de secondes, le temps du démarrage à froid.

Le FQDN du bastion n'est pas un output Terraform : il se lit sur la Container App.

```powershell
az account set --subscription $SUB_PROD
$bastion = az containerapp show --name ca-bastion-prod --resource-group rg-houseflow-prod `
  --query properties.configuration.ingress.fqdn -o tsv
$pg = az postgres flexible-server show --name psql-houseflow-prod --resource-group rg-houseflow-prod `
  --query fullyQualifiedDomainName -o tsv

# Token Entra — il tient environ une heure, et sert de mot de passe.
$token = az account get-access-token --resource-type oss-rdbms --query accessToken -o tsv

ssh -N -L 5432:${pg}:5432 bastion@$bastion -p 2222
```

Dans un autre terminal :

```powershell
$env:PGPASSWORD = $token
psql "host=localhost port=5432 dbname=houseflow_prod user=$ENTRA_NAME sslmode=require"
```

La base porte le nom de l'instance, tirets remplacés par des soulignés : `houseflow_prod`,
`houseflow_pr_123`.

**Via DBeaver** — onglet SSH : hôte `$bastion`, port `2222`, utilisateur `bastion`,
authentification par clé publique (`~/.ssh/id_ed25519`). Onglet Main : hôte `$pg`, port `5432`,
base `houseflow_prod`, utilisateur ton UPN Microsoft, mot de passe le token ci-dessus.

## 11. Recréer le Key Vault après une destruction

`kv-houseflow` reste sept jours en soft-delete, et son nom demeure réservé pendant ce temps : le
prochain `apply-shared` échoue à la création. Deux issues, et elles ne se valent pas.

```powershell
az keyvault recover --name kv-houseflow --location $LOCATION   # rend le coffre ET son certificat
az keyvault purge   --name kv-houseflow --location $LOCATION   # libère le nom, perd le certificat
```

Préférer `recover` : purger oblige à réémettre le certificat, donc à consommer une des cinq
émissions hebdomadaires autorisées par Let's Encrypt. Troisième issue si ni l'une ni l'autre
n'est praticable — la purge demande un administrateur de la souscription : repartir sous un autre
nom de coffre, ce que la variable `key_vault_name` de la racine `shared` autorise (voir la mise
en garde du §8c sur les trois valeurs à changer ensemble). Le provider azurerm est délibérément
configuré avec `purge_soft_delete_on_destroy = false` et `recover_soft_deleted_key_vaults =
false` — ces deux options appellent l'API des coffres supprimés, de niveau souscription, et c'est
une décision qui mérite un humain.

## Récapitulatif des protections

```
Couche 0 — Souscriptions    Production et jetable séparées ; aucune identité des deux côtés
Couche 1 — Identités        Un service principal par environnement GitHub, OIDC, sans secret statique
Couche 2 — GitHub           Environnements sensibles limités à `main` ; gate `prod-approval`
Couche 3 — Pipeline         Plan publié et lu AVANT l'approbation ; apply du fichier de plan approuvé
Couche 4 — Azure RBAC       Rôles custom sans `roleAssignments/write` ; state par conteneur
Couche 5 — Azure Policy     Allowlist de types de ressources + SKU PostgreSQL burstable
Couche 6 — Terraform        `tf-plan-guard.sh` refuse la destruction d'une ressource protégée
Couche 7 — Locks            `CanNotDelete` sur le resource group et la base de production
Couche 8 — Reaper           Suppression par tag `ttl` ; la production n'en porte pas
Couche 9 — Réseau           Un VNet par environnement, PostgreSQL sans accès public
Couche 10 — Auth            Entra ID exclusif, aucun mot de passe de base de données
```
