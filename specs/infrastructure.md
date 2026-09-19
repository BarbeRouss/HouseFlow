# Infrastructure HouseFlow — cible

Ce document décrit l'infrastructure Azure et le pipeline de déploiement (le QUOI). Il ne porte
aucun statut d'avancement. Domaine : `houseflow.cloud`. Région : `westeurope`. Préfixe : `houseflow`.

## Principes

1. **Une frontière = une identité.** Chaque environnement (preprod, preview, prod) a son app
   registration GitHub, son resource group, son VNet, son Container Apps Environment (CAE) et son
   identité managée. Un run preprod ne peut rien faire dans `rg-houseflow-prod`.
2. **`shared` ne contient que de la donnée et des secrets** : le serveur PostgreSQL (seul coût
   fixe), le Key Vault, le state Terraform, les dumps. Il est appliqué par l'identité prod, derrière
   la gate.
3. **Une seule approbation par push**, portée par un job vide sur un environnement GitHub dédié
   (`prod-approval`), jamais par les jobs qui font le travail.
4. **Pas de `terraform_remote_state` entre stacks** : les références croisées passent par des data
   sources sur des noms fixes. Un service principal ne lit que ses propres states.

## Resource groups et réseau

```
rg-houseflow-shared   vnet-houseflow-shared   10.0.0.0/24   snet-db 10.0.0.0/28 (délégation PostgreSQL)
                      psql-houseflow · houseflow.private.postgres.database.azure.com (private DNS zone)
                      kv-houseflow · sthouseflowtfstate (bootstrap) · conteneur blob db-dumps
                      id-houseflow-preprod · id-houseflow-preview · id-houseflow-prod

rg-houseflow-preprod  vnet-houseflow-preprod  10.1.0.0/16   snet-cae 10.1.0.0/23   ─peering─► vnet-houseflow-shared
                      cae-houseflow-preprod · log-houseflow-preprod · ca-bastion-preprod
                      ca-api-preprod · ca-frontend-preprod

rg-houseflow-preview  vnet-houseflow-preview  10.2.0.0/16   snet-cae 10.2.0.0/23   ─peering─► vnet-houseflow-shared
                      cae-houseflow-preview · log-houseflow-preview
                      ca-api-pr-<n> · swa-pr-<n> (Static Web App, frontend)

rg-houseflow-prod     vnet-houseflow-prod     10.3.0.0/16   snet-cae 10.3.0.0/23   ─peering─► vnet-houseflow-shared
                      cae-houseflow-prod · log-houseflow-prod · ca-bastion-prod
                      ca-api-prod · ca-frontend-prod · lock CanNotDelete sur le RG
```

- Chaque stack `env` crée **les deux côtés** de son peering (`vnet-<env>` ⇄ `vnet-shared`) et le lien
  de la private DNS zone vers son VNet (sinon le FQDN privé du serveur ne résout pas).
- Les identités managées vivent dans `rg-houseflow-shared` (créées par le stack `shared`) pour que le
  stack `shared` puisse leur assigner du RBAC (Key Vault, blob) sans dépendre des stacks `env`. Les
  stacks `env` les attachent à leurs apps (`userAssignedIdentities/assign/action`).
- Le peering intra-région est facturé au Go transféré (négligeable). VNet, subnets, CAE Consumption,
  identités : aucun coût fixe. Log Analytics : au Go ingéré.

## Identités et RBAC

| Environnement GitHub | App registration              | Federated credential (subject)                        | Rôles |
|---|---|---|---|
| `preprod`            | `houseflow-github-preprod`    | `repo:BarbeRouss/HouseFlow:environment:preprod`       | `HouseFlow Deployer` sur `rg-houseflow-preprod` ; `HouseFlow Shared Tenant` sur `rg-houseflow-shared` |
| `preview`            | `houseflow-github-preview`    | `repo:BarbeRouss/HouseFlow:environment:preview`       | `HouseFlow Deployer` sur `rg-houseflow-preview` ; `HouseFlow Shared Tenant` sur `rg-houseflow-shared` |
| `prod`               | `houseflow-github-prod`       | `repo:BarbeRouss/HouseFlow:environment:prod`          | `HouseFlow Deployer` sur `rg-houseflow-prod` et `rg-houseflow-shared` ; `Role Based Access Control Administrator` sur `rg-houseflow-shared` **conditionné** aux rôles `Key Vault Secrets User`, `Key Vault Certificates Officer`, `Storage Blob Data Reader`, `Storage Blob Data Contributor` ; `Key Vault Certificates Officer` + `Key Vault Secrets Officer` sur `rg-houseflow-shared`, hérités par `kv-houseflow` (émission du certificat, posés au bootstrap avant que le vault existe) |
| `prod-approval`      | aucune                        | aucune                                                | aucun — gate pure (required reviewers, branche `main` uniquement) |
| tous                 |                               |                                                       | `HouseFlow Deployer (subscription)` (`Microsoft.Web/locations/*/read`) pour `preview` (Static Web Apps) |

Rôles custom versionnés dans `infrastructure/rbac/` (placeholder `<SUBSCRIPTION_ID>`) :

- **`HouseFlow Deployer`** (`houseflow-deployer.role.json`) : plan de gestion des types que Terraform
  crée dans un RG d'environnement — Container Apps (apps, environnements, **jobs**, certificats
  d'environnement), Static Web Apps, PostgreSQL (bases), Log Analytics, Storage, Network (VNet,
  subnets, **peerings**), Identity (**assign/action**), Key Vault, locks. Pas de `roleAssignments/write`.
- **`HouseFlow Shared Tenant`** (`houseflow-shared-tenant.role.json`) — ce qu'un environnement non-prod
  a le droit de faire dans `rg-houseflow-shared`, et rien d'autre :
  `Microsoft.Network/virtualNetworks/read`, `Microsoft.Network/virtualNetworks/peer/action`,
  `Microsoft.Network/virtualNetworks/virtualNetworkPeerings/read|write|delete`,
  `Microsoft.Network/privateDnsZones/read`, `Microsoft.Network/privateDnsZones/virtualNetworkLinks/read|write|delete`,
  `Microsoft.ManagedIdentity/userAssignedIdentities/read|assign/action`,
  `Microsoft.DBforPostgreSQL/flexibleServers/read`, `Microsoft.KeyVault/vaults/read`,
  `Microsoft.Storage/storageAccounts/read`, `Microsoft.Storage/storageAccounts/blobServices/containers/read`.

Le RBAC **data-plane** (Key Vault, blob) est posé par le stack `shared` sur les identités managées :

| Identité             | Key Vault (scope : le secret du certificat `wildcard-houseflow-cloud`) | Blob `db-dumps` |
|---|---|---|
| `id-houseflow-prod`    | Key Vault Secrets User | Storage Blob Data Contributor |
| `id-houseflow-preprod` | Key Vault Secrets User | Storage Blob Data Reader |
| `id-houseflow-preview` | Key Vault Secrets User | Storage Blob Data Reader |

## PostgreSQL : un serveur, une frontière par rôles

`psql-houseflow` (Flexible Server 16, `B_Standard_B1ms`, 32 Go, accès privé, backups 7 jours) est
partagé. La séparation est au niveau PostgreSQL :

| Rôle PostgreSQL         | Créé par | Droits |
|---|---|---|
| utilisateur Entra admin | stack `shared` (`ENTRA_ADMIN_*`) | admin (`az login` + `psql` via bastion) |
| `id-houseflow-prod`     | stack `shared` (administrateur Entra) | admin — seule identité applicative admin |
| `id-houseflow-preprod`  | job `dbtools roles` | propriétaire de `houseflow_preprod` ; aucun grant ailleurs |
| `id-houseflow-preview`  | job `dbtools roles` | attribut `CREATEDB` ; propriétaire des bases `houseflow_pr_<n>` qu'elle crée ; aucun grant ailleurs |

- `houseflow_prod` : créée par le stack `deploy-prod` (ARM), avec son lock `CanNotDelete`.
- `houseflow_preprod` : créée en SQL par `dbtools roles` (`OWNER id-houseflow-preprod`) — pas via ARM.
- `houseflow_pr_<n>` : créée/supprimée en SQL par `dbtools init` dans le CAE preview, en `id-houseflow-preview`.
- Le runner GitHub n'a **aucun chemin réseau** vers le serveur : tout SQL d'administration passe par
  des Container Apps Jobs dans les CAE (VNet peerés).
- La copie prod → preprod/previews (dump nocturne pseudonymisé, restauration par environnement) est
  spécifiée dans l'issue #199 et utilise la même image `dbtools` (`dump`, `restore`) et le conteneur
  `db-dumps`.

## Image `dbtools`

`dbtools/Dockerfile` : `postgres:16-alpine` + `bash`, `curl`, `jq` (pas d'`azure-cli` : aucune
sous-commande livrée n'en a besoin et il pèse 700 Mo ; `dump`/`restore` ajouteront `azcopy`, #199).
Publiée par le job `build` sur `ghcr.io/barberouss/houseflow-dbtools:<tag>` comme les images
api/frontend. Authentification exclusivement par identité managée (token Entra
`https://ossrdbms-aad.database.windows.net` obtenu sur l'endpoint d'identité du conteneur). Sous-commandes (`dbtools <cmd>`), toutes idempotentes :

| Commande  | Où | Identité | Effet |
|---|---|---|---|
| `roles`   | CAE prod (`job-dbtools-roles`)    | `id-houseflow-prod`    | `pgaadauth_create_principal` pour `id-houseflow-preprod` et `id-houseflow-preview` ; `CREATE DATABASE houseflow_preprod OWNER id-houseflow-preprod` si absente ; `ALTER ROLE "id-houseflow-preview" CREATEDB` |
| `init`    | CAE preview (`job-dbtools-init`)  | `id-houseflow-preview` | `CREATE DATABASE houseflow_pr_<n>` (env `PR_NUMBER`) ; `ACTION=drop` → `DROP DATABASE … WITH (FORCE)` |
| `dump`    | CAE prod (cron)                   | `id-houseflow-prod`    | #199 |
| `restore` | CAE preprod / preview             | `id-houseflow-<env>`   | #199 |

Le pipeline lance un job avec `az containerapp job start … --env-vars` puis attend la fin de
l'exécution (`az containerapp job execution show`) ; une exécution en échec fait échouer le job GitHub.

## Certificat TLS

Certificat wildcard `*.houseflow.cloud` + `houseflow.cloud`, Let's Encrypt, validation DNS-01 contre
la zone OVH (lego, compte ACME persisté dans le secret `acme-account` du Key Vault).

- **Émission** : job `certificate` de `pipeline.yml`, environnement `prod`, après `apply-shared` ;
  aussi sur cron (1er du mois, 03:00 UTC) et sur dispatch (`force_certificate`). Idempotent : sans
  `force`, n'émet que si le certificat Key Vault manque, expire dans moins de 30 jours ou vient du
  serveur ACME de staging. Résultat : certificat Key Vault `wildcard-houseflow-cloud` (PFX exporté
  en 3DES/SHA1, seul format accepté par Container Apps).
- **Consommation** : chaque CAE référence le certificat **directement dans Key Vault**
  (`Microsoft.App/managedEnvironments/certificates` avec `certificateKeyVaultProperties` : URL du
  secret sans version + identité managée de l'environnement), via la ressource `azapi`. Une nouvelle
  version dans Key Vault est reprise automatiquement : le renouvellement ne redéploie rien.
- Les domaines custom (`azurerm_container_app_custom_domain`) se lient à ce certificat
  d'environnement ; les Static Web Apps des previews gèrent leur propre certificat managé.

## DNS (OVH, zone `houseflow.cloud`)

Stack `dns`, appliqué par l'environnement `prod`, après les trois stacks `env` (il lit le
`custom_domain_verification_id` et le domaine par défaut de chaque CAE par data source) :

| Enregistrement | Cible |
|---|---|
| `www`, `asuid.www`             | `ca-frontend-prod` (CAE prod) |
| `api`, `asuid.api`             | `ca-api-prod` (CAE prod) |
| `preprod`, `asuid.preprod`     | `ca-frontend-preprod` (CAE preprod) |
| `api-preprod`, `asuid.api-preprod` | `ca-api-preprod` (CAE preprod) |
| `pr-<n>`, `api-pr-<n>`, `asuid.api-pr-<n>` | gérés par le stack `ephemeral` (module `ovh-dns-zone`), CAE preview |

Garde-fou conservé : un plan qui supprime des enregistrements est refusé sans le marqueur
`[dns-allow-destroy]` dans le commit ou l'input `allow_dns_destroy` du dispatch.

## Stacks Terraform

```
infrastructure/terraform/
├── shared/           rg-houseflow-shared : VNet + snet-db, PostgreSQL (+ admins Entra : utilisateur, id-prod),
│                     private DNS zone, Key Vault (RBAC), conteneur db-dumps, 3 identités, RBAC KV/blob
├── modules/env/      un environnement : VNet + snet-cae, peering ⇄ shared (2 côtés), lien private DNS,
│                     Log Analytics, CAE (+ certificat par référence KV via azapi), bastion (flag),
│                     lock RG (flag), jobs dbtools (liste), outputs (cae id/domain/verification id, identity)
├── env-preprod/      module env : bastion=true, rg_lock=false, jobs=[]
├── env-preview/      module env : bastion=false, rg_lock=false, jobs=[init]
├── env-prod/         module env : bastion=true, rg_lock=true,  jobs=[roles]
├── deploy-preprod/   ca-api-preprod, ca-frontend-preprod, domaines custom (base : créée par dbtools roles)
├── deploy-prod/      ca-api-prod, ca-frontend-prod, domaines custom, houseflow_prod (ARM) + lock, locks apps
├── ephemeral/        par PR : ca-api-pr-<n> (CAE preview), swa-pr-<n>, DNS (module ephemeral-env)
├── dns/              enregistrements OVH prod/preprod (module ovh-dns-zone)
└── modules/ephemeral-env, modules/ovh-dns-zone   (existants, adaptés)
```

Conventions communes :

- Provider `azurerm ~> 4`, `azapi ~> 2` (certificats d'environnement), `ovh ~> 2`. `use_oidc = true`,
  `resource_provider_registrations = "none"`. Features Key Vault : pas de purge ni de recover
  automatiques (hors portée du rôle).
- Backend `azurerm` sur `sthouseflowtfstate` (`rg-houseflow-shared`), `use_oidc = true` :

  | Conteneur         | Clés | Écrit par |
  |---|---|---|
  | `tfstate-shared`  | `shared.tfstate`, `dns.tfstate` | `prod` |
  | `tfstate-prod`    | `env-prod.tfstate`, `deploy-prod.tfstate` | `prod` |
  | `tfstate-nonprod` | `env-preprod.tfstate`, `deploy-preprod.tfstate`, `env-preview.tfstate`, `ephemeral-pr-<n>.tfstate` | `preprod`, `preview` |

- Références croisées par **data sources sur noms fixes** (ex. `data "azurerm_virtual_network"
  "shared"`, `data "azurerm_container_app_environment" "this"`, `data "azurerm_user_assigned_identity"`,
  `data "azurerm_key_vault"`). Aucun `terraform_remote_state`.
- Garde-fou conservé sur `apply-shared` et `env-prod` : refus d'un plan qui détruit
  `azurerm_container_app_environment`, `azurerm_postgresql_flexible_server`,
  `azurerm_postgresql_flexible_server_database`, `azurerm_key_vault`, `azurerm_virtual_network`,
  `azurerm_subnet`, `azurerm_user_assigned_identity`, `azurerm_log_analytics_workspace`.
- Variables secrètes par `TF_VAR_*` depuis les secrets GitHub ; `JWT_KEY` et
  `BASTION_SSH_PUBLIC_KEY` sont des **secrets d'environnement** (valeurs distinctes preprod/prod).

## Pipeline (`.github/workflows/pipeline.yml`)

Déclencheurs : `push` sur `main`, `workflow_dispatch` (inputs `force_infra`, `force_certificate`,
`allow_dns_destroy`), `schedule` (cron certificat). Actions épinglées sur SHA.

```
detect ─┬─ build (api, frontend, dbtools ; tag CalVer réservé push-first)
        ├─ [shared ou env-prod modifiés, force_infra] approve-infra ─ apply-shared ─ env-prod ─ dbtools-roles ─┐
        ├─ [env-preprod modifié, force_infra] env-preprod ──────────────────────────────────────────────────────┤
        ├─ [env-preview modifié, force_infra] env-preview ──────────────────────────────────────────────────────┤
        └─ certificate (après apply-shared ; cron ; force) ─ dns (après env-*, dns modifié) ───────────────────┤
                                                                                                deploy-preprod ┘
                                                                                                       │
                                                                     approve-prod (skippé si approve-infra a tourné)
                                                                                                       │
                                                                                                  deploy-prod
```

| Job | `environment` | Conditions et notes |
|---|---|---|
| `detect`         | —              | `dorny/paths-filter` : `shared`, `env_prod`, `env_preprod`, `env_preview`, `dns`, `dbtools`, `app` ; `dbtools` implique `env_prod` et `env_preview` (le tag de l'image est une variable de ces stacks) ; cron ⇒ `certificate` seul ; base inconnue (premier push) ⇒ tout |
| `build`          | —              | images api / frontend / dbtools ; sortie `version` |
| `approve-infra`  | `prod-approval`| job vide ; `if` shared/env_prod/force ; `concurrency: { group: approve, cancel-in-progress: true }` |
| `apply-shared`   | `prod`         | plan + garde-fou destruction + apply |
| `env-prod`       | `prod`         | idem |
| `dbtools-roles`  | `prod`         | `az containerapp job start job-dbtools-roles` + attente |
| `env-preprod`    | `preprod`      | plan + apply |
| `env-preview`    | `preview`      | plan + apply |
| `certificate`    | `prod`         | lego → Key Vault ; `needs: [apply-shared]` toléré skippé |
| `dns`            | `prod`         | `needs: [env-prod, env-preprod, env-preview]` tolérés skippés ; garde-fou DNS |
| `deploy-preprod` | `preprod`      | `needs: [build, dbtools-roles, env-preprod, certificate, dns]` tolérés skippés (jamais échoués) ; apply + health check |
| `approve-prod`   | `prod-approval`| job vide ; `needs: [deploy-preprod, approve-infra]`, `if: needs.approve-infra.result == 'skipped'` (rien à approuver sur le cron) ; même groupe de concurrence |
| `deploy-prod`    | `prod`         | `needs: [build, deploy-preprod, approve-infra, approve-prod]` — au moins une approbation réussie ; `concurrency: { group: deploy-prod, cancel-in-progress: false }` |

- Idiome pour les jobs aval de jobs skippés :
  `if: always() && !cancelled() && needs.X.result == 'success' && contains(fromJSON('["success","skipped"]'), needs.Y.result)`.
- Un nouveau push annule l'approbation en attente du précédent (job `approve-*` seul) ; jamais un
  apply en cours.
- **`pr-preview.yml`** : environnement `preview`, stack `ephemeral` (backend `tfstate-nonprod`),
  puis `job-dbtools-init` (`PR_NUMBER`) ; à la fermeture : `ACTION=drop` puis destroy.

## Bootstrap (manuel, une fois — détail dans `docs/azure-setup-guide.md`)

1. Resource providers ; 4 resource groups.
2. 3 app registrations + service principals, 1 federated credential chacune.
3. Rôles custom depuis `infrastructure/rbac/` ; role assignments du tableau ci-dessus.
4. `sthouseflowtfstate` dans `rg-houseflow-shared`, conteneurs `tfstate-shared`, `tfstate-nonprod`,
   `tfstate-prod` ; `Storage Blob Data Contributor` par conteneur selon le tableau des states.
5. Policies souscription (allowlist de types — ajouter `Microsoft.App/jobs`,
   `Microsoft.Network/virtualNetworks/virtualNetworkPeerings`, `Microsoft.Network/privateDnsZones/virtualNetworkLinks` — et SKU PostgreSQL).
6. Environnements GitHub `preprod`, `preview`, `prod` (sans reviewers), `prod-approval` (required
   reviewers, `main` uniquement) ; secrets d'environnement `AZURE_CLIENT_ID` (×3), `JWT_KEY`,
   `BASTION_SSH_PUBLIC_KEY` ; secrets de repo `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID`, `GHCR_PAT`,
   `OVH_APPLICATION_SECRET`, `OVH_CONSUMER_KEY`, `ENTRA_ADMIN_OBJECT_ID`, `ENTRA_ADMIN_NAME` ;
   variables `OVH_APPLICATION_KEY`, `LETSENCRYPT_EMAIL`.
7. Avant le premier apply après une suppression : `az keyvault purge --name kv-houseflow`
   (soft-delete 7 jours), et zone OVH vidée des anciens enregistrements.
