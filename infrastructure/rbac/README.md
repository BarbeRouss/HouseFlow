# RBAC HouseFlow — qui a le droit de faire quoi, et où

La frontière est la **souscription**. La production et les environnements jetables vivent dans
deux souscriptions distinctes du même tenant : `sp-prod` n'a aucun rôle dans la souscription
jetable, et `sp-preview` n'en a aucun dans celle de production. C'est une limite qu'aucun tag mal
posé ni aucun bug de filtre ne peut franchir, là où un découpage en resource groups supposait que
tout le monde vise le bon.

Ce n'est pas un raffinement : depuis qu'un environnement crée et détruit ses propres resource
groups, le rôle qui le permet s'assigne forcément à la souscription. Un service principal capable
de faire `az group delete` dans la souscription de production serait à une erreur de filtre de la
détruire.

Deux mécanismes à ne pas confondre :

- **Federated credential** = une *confiance*. Elle dit quel workflow GitHub peut demander un token
  au nom de cette identité (subject `repo:BarbeRouss/HouseFlow:environment:<env>`). Elle ne donne
  accès à aucune ressource.
- **Role assignment** = un *droit*, posé sur le service principal, à un scope donné.

C'est pourquoi `prod-approval` n'a **ni** app registration **ni** federated credential : c'est une
gate d'approbation humaine, elle n'accède à rien.

Deux identités seulement, parce qu'il ne reste que deux environnements GitHub qui touchent Azure.
`preprod` a disparu avec l'environnement de validation qu'il portait : l'environnement d'une PR
étant complet, il éprouve les changements d'infrastructure dans la PR qui les introduit. Si
l'app registration `houseflow-github-preprod` et son service principal existent encore dans le
tenant, plus rien ne demande de token en leur nom — aucun workflow ne référence l'environnement
`preprod` — et leurs assignations de rôle peuvent être supprimées.

## Vue d'ensemble

```mermaid
graph LR
  EA(["env GitHub<br/>prod-approval"])

  EO(["env GitHub<br/>prod"]) -->|OIDC| SPO["sp houseflow-github-prod"]
  EV(["env GitHub<br/>preview"]) -->|OIDC| SPV["sp houseflow-github-preview"]

  subgraph SUBP ["souscription production"]
    RGS["rg-houseflow-shared-prod<br/>kv-houseflow · states · db-dumps<br/>id-houseflow-cert-prod · id-houseflow-dumps-writer"]
    RGO["rg-houseflow-prod<br/>créé par Terraform"]
  end

  subgraph SUBE ["souscription jetable"]
    RGSE["rg-houseflow-shared-ephemeral<br/>states · id-houseflow-cert-ephemeral · id-houseflow-dumps-reader"]
    RGX["rg-houseflow-pr-&lt;n&gt;<br/>créés et détruits par Terraform"]
  end

  SPO -->|Deployer + Deployer subscription| SUBP
  SPO -->|KV Certificates + Secrets Officer| RGS
  SPO -->|RBAC Administrator conditionné| RGS

  SPV -->|Deployer + Deployer subscription| SUBE

  IDC["id-houseflow-cert-ephemeral<br/>souscription jetable"] -.->|Key Vault Secrets User<br/>sur LE secret| RGS
  IDD["id-houseflow-dumps-reader<br/>souscription jetable"] -.->|Storage Blob Data Reader<br/>sur db-dumps| RGS
```

`prod-approval` n'a aucune flèche : aucune identité, aucun droit, rien qu'une approbation requise.

Les deux seules flèches qui traversent la frontière sont en pointillés, et elles vont du jetable
vers le permanent : l'identité de certificat de la souscription jetable lit **un secret** dans le
coffre de production, et son identité de dumps lit **un conteneur** — celui du dump pseudonymisé.
Le sens compte — aucune identité de production n'a quoi que ce soit en face, et rien du côté
jetable ne peut écrire en face.

## Rôles par scope

| Scope | `sp-prod` | `sp-preview` |
|---|---|---|
| souscription **production** | **Deployer** + **Deployer (subscription)** | — |
| `rg-houseflow-shared-prod` | `Key Vault Certificates Officer` + `Key Vault Secrets Officer` + `Role Based Access Control Administrator` (conditionné) | — |
| conteneur `tfstate` | `Storage Blob Data Contributor` | — |
| souscription **jetable** | — | **Deployer** + **Deployer (subscription)** |
| conteneur `tfstate` (jetable) | — | `Storage Blob Data Contributor` |

`sp-preview` porte aussi le `reaper.yml` : le workflow tourne sous l'environnement GitHub
`preview` parce que c'est là que se trouve le droit de supprimer un resource group. Il n'a ainsi
aucun chemin vers la production — même un bug de filtre ne pourrait pas la viser, son token ne
valant rien dans cette souscription.

`Storage Blob Data Contributor` est posé **par conteneur** et jamais sur le compte : les deux
racines Terraform s'authentifient en AAD sur le plan de données (`use_azuread_auth` dans le bloc
`backend`), et le nettoyage du state par `pr-preview.yml` et `reaper.yml` passe par
`az storage blob --auth-mode login`. Aucun de ces chemins ne demande les clés du compte.

## Ce que contient chaque rôle custom

Les JSON versionnés ici sont la source de vérité ; ces tableaux n'en sont qu'un résumé.

### `HouseFlow Deployer` — `houseflow-deployer.role.json`

Tout le plan de gestion d'un environnement HouseFlow, le resource group compris.

| Domaine | Actions |
|---|---|
| Resource groups | `subscriptions/resourceGroups/read`, `/write`, `/delete` |
| Container Apps | `Microsoft.App/*` (apps, environnements, jobs, certificats d'environnement) |
| Static Web Apps | `Microsoft.Web/staticSites/*` |
| PostgreSQL | `Microsoft.DBforPostgreSQL/flexibleServers/*` |
| Observabilité | `Microsoft.OperationalInsights/workspaces/*` |
| Réseau | `virtualNetworks/*`, `privateDnsZones/*`, `networkSecurityGroups/*` |
| Identité | `Microsoft.ManagedIdentity/userAssignedIdentities/*` |
| Key Vault | `vaults/read`, `/write`, `/delete` — **plan de gestion uniquement** |
| Storage | `storageAccounts/read`, `listKeys/action`, `blobServices/containers/*` |
| Divers | `Microsoft.Resources/deployments/*`, `Microsoft.Authorization/locks/*` |

`resourceGroups/write` et `/delete` sont ce qui a changé de nature avec la refonte, et ce qui
impose le scope souscription : le nom du resource group d'un environnement jetable n'est pas
connu avant sa création, donc aucune assignation plus étroite n'est possible. La contrepartie est
assumée, et c'est elle qui justifie les deux souscriptions.

Pas de `Microsoft.Authorization/roleAssignments/write` : une identité de déploiement ne peut pas
s'élargir elle-même. Les deux seules attributions de rôle de toute l'infrastructure passent par
le rôle conditionné décrit plus bas.

`Microsoft.Network/virtualNetworks/*` couvre `subnets/join/action`, nécessaire à la délégation
des deux subnets au Flexible Server et au Container Apps Environment. Ce droit est désormais sans
danger pour les autres environnements : le VNet appartient à celui qui le crée, et il n'y a aucun
peering entre eux.

> **Pourquoi les rôles Key Vault de `sp-prod` sont séparés et non fusionnés ici.** Ce rôle est
> strictement **plan de gestion** : créer, configurer et détruire le coffre en tant que ressource.
> Importer un certificat ou écrire un secret relève du **plan de données** (`dataActions`), porté
> par les rôles intégrés `Key Vault Certificates Officer` et `Key Vault Secrets Officer`, assignés
> à `sp-prod` sur `rg-houseflow-shared-prod` seulement. Les fusionner aurait deux défauts : la liste
> d'actions d'un rôle intégré est maintenue par Microsoft et suit l'évolution du service, alors
> qu'une copie fige celle du jour ; et comme `HouseFlow Deployer` porte maintenant sur la
> souscription entière, tout coffre créé plus tard dans n'importe quel resource group donnerait
> d'office accès à son contenu.

### `HouseFlow Deployer (subscription)` — `houseflow-deployer-subscription.role.json`

`Microsoft.Web/locations/*/read`, et rien d'autre. Lier le domaine custom d'une Static Web App est
une opération longue dont Azure publie l'état hors resource group : sans ce droit, l'apply échoue
en `AuthorizationFailed` au moment du bind, après avoir créé toutes les ressources.

Le wildcard est délibéré : l'action exacte (`staticSitesOperationStatuses/read`) n'est pas publiée
dans le registre d'opérations du provider et `az role definition create` la refuse
(`InvalidActionOrNotAction`).

Les deux identités en ont besoin, puisque le frontend est une Static Web App partout, production
comprise. Le rôle est en lecture seule.

### `HouseFlow Shared Tenant` — supprimé

Ce rôle décrivait ce qu'un environnement non-prod avait le droit de faire dans
`rg-houseflow-shared-prod`. Il n'a plus d'objet et sa définition a été retirée du dépôt ; si une
assignation subsiste dans le tenant, elle peut être supprimée sans conséquence.

La raison est directe : la racine `environment` ne lit plus le Key Vault par data source. L'URI
du coffre est dérivée de son nom (`TF_VAR_key_vault_name`, alimentée par la variable de dépôt
`KEY_VAULT_NAME`), précisément pour qu'un environnement jetable n'ait aucun droit de plan de
gestion sur le coffre de production — qui vit d'ailleurs dans l'autre souscription, où ce rôle
n'était de toute façon pas assignable.

Les autres besoins qu'il couvrait ont disparu avec le serveur partagé : il n'y a plus de subnet
d'autrui à joindre, plus de serveur PostgreSQL commun à lire, plus d'identité d'environnement
hébergée ailleurs que chez soi. Tout ce que la racine `environment` lit encore dans le resource
group partagé de sa souscription (`rg-houseflow-shared-prod` ou `-ephemeral`), ce sont
`id-houseflow-cert-prod`/`-ephemeral` et `id-houseflow-dumps-reader` (`id-houseflow-dumps-writer`
en production) — un `userAssignedIdentities/read` (et le `assign/action` qui permet de les
attacher) que `HouseFlow Deployer` couvre déjà au scope souscription.

Sur une installation neuve, il n'y a rien à créer : la définition ne fait plus partie du dépôt.

## Les attributions de rôle de l'infrastructure

La racine `shared` crée **deux** attributions, et ce sont les seules de tout le design :

| Identité managée | Rôle | Scope exact |
|---|---|---|
| `id-houseflow-cert-prod` | `Key Vault Secrets User` | le secret `wildcard-houseflow-cloud`, pas le coffre |
| `id-houseflow-dumps-writer` | `Storage Blob Data Contributor` | le conteneur `db-dumps`, pas le compte |

Tout tient à cette indirection. Chaque Container Apps Environment attache `id-houseflow-cert-prod`
ou `id-houseflow-cert-ephemeral` (selon sa souscription) pour résoudre sa référence Key Vault, et
chaque job `dbtools` attache `id-houseflow-dumps-writer` ou `id-houseflow-dumps-reader` pour
atteindre le dump, au lieu d'utiliser l'identité de l'environnement. Si l'identité de
l'environnement devait lire le coffre, il faudrait lui attribuer un rôle **à chaque création** —
donc confier au service principal de déploiement le pouvoir de distribuer des rôles, ce que
`HouseFlow Deployer` refuse délibérément. Un environnement jetable serait alors soit
impossible à créer, soit créé par une identité capable de s'octroyer n'importe quoi.

Une identité, un rôle, attribué une fois. Les environnements n'en héritent que l'usage.

C'est pour poser ces deux attributions que `sp-prod` porte un `Role Based Access Control
Administrator` **conditionné** (ABAC) sur `rg-houseflow-shared-prod`, restreint par condition à
ces deux rôles. Il ne peut ni s'octroyer Owner, ni promouvoir une autre identité au-delà.

Les homologues de la souscription jetable reçoivent leurs droits **au bootstrap et à la main**
(`docs/azure-setup-guide.md` §5 et §5a) : aucun stack ne franchit la frontière des souscriptions.
Et ces droits sont plus étroits : `id-houseflow-dumps-reader` n'a que `Storage Blob Data Reader` sur `db-dumps`. Le
nom annonce le droit — le module `environment` prend `-writer` sur l'instance permanente et
`-reader` sur une PR —, mais c'est la souscription qui le garantit : la jetable ne contient
aucune identité capable d'écrire. Une PR qui détournerait le code ne peut donc pas substituer le dump que
les autres environnements restaureront.

## Identités d'environnement — aucun rôle Azure

`id-houseflow-<nom>` vit dans le resource group de son environnement et n'a **aucune** attribution
RBAC Azure. Son habilitation est ailleurs : elle est administratrice Entra de **son** serveur
PostgreSQL, et de nul autre, et c'est le nom de son rôle PostgreSQL.

C'est ce qui fait disparaître le job `dbtools roles` : il n'existe plus d'identité privilégiée
chargée de créer les rôles des autres environnements, puisqu'aucun environnement ne partage plus
de serveur. La base applicative est créée par Terraform.

## State Terraform — une frontière d'écriture par conteneur

| Souscription | Conteneur | Clés | Écrit par |
|---|---|---|---|
| production | `tfstate` | `environment-prod.tfstate`, `dns-prod.tfstate`, `custom-domains-prod.tfstate` | `sp-prod` |
| production | `tfstate` | `shared.tfstate` | `sp-prod` |
| jetable | `tfstate` | `environment-pr-<n>.tfstate`, `dns-pr-<n>.tfstate`, `custom-domains-pr-<n>.tfstate` | `sp-preview` |

Le nom du storage account n'est nulle part dans le code : il arrive en `-backend-config` depuis
le secret d'environnement `TFSTATE_STORAGE_ACCOUNT`. Un nom de storage account est unique au
niveau mondial, donc chaque souscription a forcément le sien — ce qui interdit structurellement
qu'un run jetable écrive dans le storage de production, indépendamment de tout RBAC.

Aucune racine ne lit le state d'une autre : il n'y a pas de `terraform_remote_state` dans ce
dépôt, les références croisées passent par des data sources sur des noms fixes.

## Ce qui empêche une PR d'atteindre la prod

La gate `prod-approval` ne protège **rien** à elle seule : c'est un job vide, et les jobs qui
travaillent portent `environment: prod` sans required reviewer. Ce qui protège la production,
c'est la **politique de branche** des environnements — `prod` et `prod-approval` limités à `main`.

Sans elle, une PR suffirait : sur un événement `pull_request`, GitHub exécute le workflow tel
qu'il est dans la branche de la PR. Un job `environment: prod` ajouté dans cette branche
obtiendrait un token OIDC de subject `repo:BarbeRouss/HouseFlow:environment:prod` — la federated
credential ne contraint que l'environnement, jamais la branche — et partirait sans approbation
avec tous les droits de `sp-prod`. Avec la politique de branche, GitHub refuse de démarrer le job
et ne délivre aucun token.

`preview` reste ouvert à toutes les branches, faute de quoi les previews de PR ne tourneraient
pas. C'est acceptable depuis que `sp-preview` n'a de droits que dans la souscription jetable.

## Ce que la frontière garantit — et ce qu'elle ne garantit pas

**Garanti.** Un run de preview ne peut rien créer, lire ni détruire dans la souscription de
production : ni resource group, ni state, ni coffre. Il ne peut pas davantage lire une base de
production — `id-houseflow-prod` est la seule administratrice Entra du serveur `psql-houseflow-prod`,
qui est de toute façon dans un VNet auquel rien ne se raccorde depuis l'autre souscription.

**Non garanti, côté jetable.** `sp-preview` porte toutes les previews à la fois : le run d'une PR
peut détruire l'environnement d'une autre, et `listKeys` sur le storage de states lui donne accès
à tous les states jetables, qui contiennent `JWT_KEY` et `GHCR_PAT` en clair. C'est assumé — ces
environnements vivent quatre heures, et ne portent que des données de démonstration et le dump
**pseudonymisé** de la prod, que n'importe quelle PR peut lire. La pseudonymisation a lieu dans
la production, avant publication, et un contrôle bloque la publication au moindre écart
(`dbtools/README.md`) : c'est elle, et non le cloisonnement des previews, qui protège les données
personnelles.

**Non garanti, côté production.** `sp-prod` est l'identité la plus privilégiée de sa souscription
et n'est contenue par rien d'autre que le pipeline : Deployer au scope souscription lui donne de
quoi détruire ce qu'un lock ne protège pas. C'est précisément pour cela que le seul chemin vers
`sp-prod` passe par un plan publié, lu, puis approuvé sur `prod-approval` — et que l'apply
consomme ce fichier de plan, pas un nouveau.

## Voir l'état réel dans Azure

```powershell
# Les rôles custom d'une souscription et leurs portées
az role definition list --custom-role-only true --query "[?starts_with(roleName,'HouseFlow')].{nom:roleName, scopes:assignableScopes}" -o table

# Tout ce qui est assigné à une identité (--all : sinon les scopes RG sont ignorés)
az role assignment list --all --assignee $APP["prod"] --query "[].{role:roleDefinitionName, scope:scope}" -o table

# Vue par resource group (rg-houseflow-shared-prod côté production,
# rg-houseflow-shared-ephemeral côté jetable)
az role assignment list --resource-group rg-houseflow-shared-prod --query "[].{qui:principalName, role:roleDefinitionName}" -o table
```

Un rôle custom appartient à une souscription : il faut répéter ces commandes après
`az account set --subscription` pour voir l'autre moitié du tableau.

Dans le portail : Abonnements → *la souscription* → Contrôle d'accès (IAM) → onglet **Rôles** pour
les définitions ; le même onglet **Attributions de rôles** sur chaque resource group pour les
assignations. La condition ABAC se lit en cliquant l'assignation → onglet **Condition**.

---

Procédure de création : [`docs/azure-setup-guide.md`](../../docs/azure-setup-guide.md) §2 à §5.
Architecture d'ensemble : [`specs/infrastructure.md`](../../specs/infrastructure.md).
