# Infrastructure HouseFlow — cible

Ce document décrit l'infrastructure Azure et le flux de déploiement (le QUOI). Il ne porte
aucun statut d'avancement. Domaine : `houseflow.cloud`. Région : `westeurope`. Préfixe : `houseflow`.

**Où tourne quoi.** Les previews de pull request tournent sur le **homelab** (docker compose sur la
LXC `houseflow-preview`, issue #348 : `docs/design/02-previews-homelab.md`, `deploy/preview/README.md`),
pas sur Azure. Azure porte la **production**. Une **preprod** Azure est **à venir (#350)** : elle
reprendra la forme « environnement complet et autonome » décrite ci-dessous. Il n'existe plus
d'environnement Azure par PR.

## Principes

1. **Un environnement possède tout ce dont il dépend** — son resource group, son réseau, son
   serveur PostgreSQL, son Container Apps Environment, son identité. Rien de ce qui peut être
   dupliqué n'est partagé. C'est ce qui rendra un changement d'infrastructure éprouvable avant la
   production : une montée de version majeure de PostgreSQL, un changement de SKU ou de subnet
   s'appliquera d'abord sur la preprod (#350), jamais sur celui qui porte la production faute
   d'autre cible.
2. **La production n'est pas un cas particulier du code.** C'est l'instance de la racine
   Terraform dont l'échéance (`expires_at`) est vide : le verrou du resource group et le réplica d'API
   maintenu s'en déduisent. La racine sait encore décrire une instance à échéance (tag `ttl`, pas de verrou,
   scale-to-zero, job `dbtools restore`) ; aucune instance, aucun workflow ne s'en sert aujourd'hui, et la
   preprod (#350) le reprendra.
3. **Ce qui reste partagé n'est ni du compute ni de la donnée** : le certificat wildcard (Let's
   Encrypt plafonne les certificats identiques à 5 par semaine, donc un environnement ne
   peut pas émettre le sien), la zone DNS, le storage des states.
4. **Pas de `terraform_remote_state`** entre environnements : les références croisées passent par des
   data sources sur des noms fixes. Un service principal ne lit que ses propres states. (Exception
   assumée : `dns` et `custom-domains` lisent le state de l'`environment` de leur propre instance.)

## Les instances

```
 PRODUCTION (permanente)              PREPROD (à venir, #350)
 ──────────────────────────           ────────────────────────────────────────
 rg-houseflow-prod                    même forme, son propre resource group
   vnet-prod                          (voir l'issue #350 ; le code des anciens
   psql-houseflow-prod                 environnements par PR est dans l'historique git)
   cae-prod · log-prod
   id-prod · ca-bastion-prod
   ca-api-prod · swa-prod
   houseflow_prod
   www · api
   🔒 lock, aucun tag ttl

            ┌── PARTAGÉ — ni compute ni donnée ─────────────────┐
            │  kv-houseflow   certificat *.houseflow.cloud      │
            │                 + compte ACME                     │
            │  id-houseflow-cert-prod/-ephemeral  seule identité │
            │                 habilitée à lire le secret du cert │
            │  id-houseflow-dumps-writer / -reader  db-dumps    │
            │  st…tfstate     les states                        │
            │                 + conteneur db-dumps (le dump     │
            │                 pseudonymisé de la nuit)          │
            │  zone OVH       houseflow.cloud                   │
            └───────────────────────────────────────────────────┘
```

Chaque environnement a son propre VNet et aucun peering n'existe entre eux : deux instances
peuvent donc porter le même plan d'adressage (`10.0.0.0/16`, `snet-db` en `/28`, `snet-cae` en
`/23`) sans se gêner, et il n'y a pas de registre de plages à tenir.

Une seule instance est déployée aujourd'hui :

| instance | échéance | lock | bastion | charge API | hôtes DNS |
|---|---|---|---|---|---|
| `prod` | ∅ (permanente) | ✓ | ✓ | 1 réplica minimum | `www`, `api` |

Les colonnes « lock » et « charge API » ne sont pas des réglages : elles se déduisent de
l'échéance (`main.tf`, locals). En faire des variables rendrait représentable l'environnement
à échéance **et** verrouillé, c'est-à-dire un resource group promis à une destruction qu'il ne peut
pas subir. Un état qu'on ne peut pas écrire est un état qu'on ne peut pas atteindre par erreur.

Les jeux de variables sont versionnés dans `infrastructure/terraform/environment/instances/`
(aujourd'hui `prod.tfvars`) : ils décrivent des environnements, pas des secrets, et sans eux la
production ne serait pas reproductible.

## Souscriptions

La production et les environnements non permanents (la preprod à venir, #350 ; la souscription
« jetable » existe et héberge déjà le storage de states et l'identité de lecture des dumps) vivent
dans des souscriptions distinctes. C'est une frontière qu'aucun tag mal posé ni aucun bug de filtre
ne peut franchir : le service principal de la souscription jetable n'a aucun rôle dans la souscription
de production.

Chaque souscription a son propre resource group partagé, nommé d'après elle —
`rg-houseflow-shared-prod` ou `rg-houseflow-shared-ephemeral` — portant son storage de states (les
noms de storage account sont uniques au niveau mondial) et ses identités partagées :
`id-houseflow-cert-prod`/`-ephemeral`, plus `id-houseflow-dumps-writer` côté production ou
`id-houseflow-dumps-reader` côté jetable. Aucun stack ne lit le state d'un autre, donc un storage
central ne rendrait service à personne.

Deux liens seulement entre les deux souscriptions, tous deux en lecture et dans le même sens. Le
certificat wildcard vit dans le Key Vault de la souscription de **production** ; l'identité de
certificat de la souscription jetable y reçoit `Key Vault Secrets User` sur le secret seul. Le
dump pseudonymisé vit dans le conteneur `db-dumps` de la production ; l'identité de dumps de la
souscription jetable y reçoit `Storage Blob Data Reader` sur ce conteneur seul. Les deux sont
attribués une fois au bootstrap. Le sens de la dépendance compte : le jetable lit le permanent,
jamais l'inverse.

## Identités et RBAC

Une identité propre par environnement, deux identités partagées par souscription, et **deux
attributions de rôle dans toute l'infrastructure** (plus leurs homologues jetables, posées au
bootstrap).

| identité | portée | rôle |
|---|---|---|
| `id-houseflow-<nom>` | resource group de l'environnement | administratrice Entra de **son** serveur PostgreSQL, et d'aucun autre. Aucun rôle RBAC Azure. |
| `id-houseflow-cert-prod` / `id-houseflow-cert-ephemeral` | resource group partagé de sa souscription (`rg-houseflow-shared-prod` / `-ephemeral`) | `Key Vault Secrets User` sur le secret du certificat. Attachée à chaque CAE pour sa référence Key Vault. |
| `id-houseflow-dumps-writer` / `id-houseflow-dumps-reader` | resource group partagé de la souscription de production / jetable | sur le conteneur `db-dumps` : `Storage Blob Data Contributor` / `Reader`. Attachée au job `dbtools` de chaque environnement. |
| service principal GitHub | souscription | `HouseFlow Deployer` |

C'est cette séparation qui rend un environnement créable sans droit d'attribution de
rôle. Si l'identité de l'environnement devait lire le Key Vault ou le dump, il faudrait lui
attribuer un rôle **à chaque création** — donc confier au service principal de déploiement le pouvoir de
distribuer des rôles, ce que `HouseFlow Deployer` n'accorde pas délibérément.

Le rôle `HouseFlow Deployer` est assignable au **scope souscription** (un environnement crée son
propre resource group) et porte
`resourceGroups/write` et `delete`. Détail dans `infrastructure/rbac/README.md`.

## PostgreSQL

Un serveur Flexible Server par environnement, `psql-houseflow-<nom>`, en accès privé
(`public_network_access_enabled = false`) dans le subnet délégué de son propre VNet, résolu par
une zone DNS privée locale à l'environnement.

Authentification **Entra ID uniquement**, pas de mot de passe. Deux administrateurs : l'utilisateur
humain (accès `psql` via le bastion) et l'identité de l'environnement, qui est aussi le nom de son
rôle PostgreSQL.

La base applicative `houseflow_<nom>` est créée par Terraform. Il n'y a plus de création de base
en SQL par un job d'administration : c'était nécessaire quand plusieurs environnements se
partageaient un serveur, ce qui n'est plus le cas.

**Le nom du serveur est un label DNS globalement unique**, d'où la contrainte sur `name` : 1 à 20
caractères, minuscules, chiffres et tirets.

## Données de prod hors production

Un environnement non permanent démarre avec une copie **pseudonymisée** de la prod de la nuit : c'est
ce qui fait valider les migrations de la branche sur des données réalistes. La donnée personnelle ne
quitte jamais la production en clair. Aujourd'hui, ce dump est consommé par les **previews du
homelab** (`preview.yml`, lecture OIDC avec `id-houseflow-dumps-reader`) ; la preprod (#350) le
consommera aussi.

```
prod     job-dbtools-dump      cron 02:00 UTC
         pg_dump → base de travail → pseudonymisation → vérification → db-dumps/latest.dump
homelab  preview.sh up         lit latest.dump (OIDC) → base de la preview → migrations de la branche
```

- **Pseudonymisation dans la production, avant publication.** Tous les comptes sont remplacés sauf
  une allow-list (`preserved_emails`, `instances/prod.tfvars`) ; un contrôle bloque la publication
  au moindre écart. Détail et procédures : `dbtools/README.md`.
- **Un seul job par instance Azure, déduit de la permanence** : `dump` sur la prod ; la racine
  Terraform sait aussi décrire un job `restore` pour une instance non permanente (la preprod). Comme le
  verrou, ce n'est pas une variable.
- **Les droits viennent de la souscription**, pas du code : `id-houseflow-dumps-writer` ne peut écrire que
  côté production ; `id-houseflow-dumps-reader` ne peut que lire. Nul ne peut substituer le dump ni
  lire la base de prod.
- **Pas une sauvegarde** : un seul `latest.dump`, remplacé chaque nuit. La sauvegarde de la prod,
  c'est le PITR de son serveur.

## Frontend : Static Web App

Blazor WebAssembly est entièrement statique — aucun rendu côté serveur, donc aucun compute à
payer. Le frontend est servi par une Static Web App en SKU **Free** (0 $), là où une Container App
maintenait un réplica en permanence. Le `wwwroot` compilé est téléversé avec le jeton de
déploiement de la Static Web App.

La Static Web App émet elle-même son certificat par délégation CNAME : elle ne consomme pas le
wildcard, qui ne sert qu'à l'API.

## Certificat TLS

Le certificat `*.houseflow.cloud` (+ apex) est émis par Let's Encrypt en validation DNS-01 contre
la zone OVH, et importé dans `kv-houseflow`. Chaque Container Apps Environment le référence
**dans le Key Vault**, sans version : un renouvellement est repris sans redéploiement.

Le compte ACME est sauvegardé dans le Key Vault et réutilisé — Let's Encrypt limite les créations
de compte par IP, et les runners GitHub partagent les leurs.

Émission idempotente : le job ne réémet que si le certificat est absent, expire dans moins de 30
jours, ou a été émis par le serveur de staging. **La limite de 5 certificats identiques par
semaine est la contrainte structurante de toute l'architecture** — c'est elle qui interdit un
certificat par environnement et impose le Key Vault partagé.

## DNS (OVH, zone `houseflow.cloud`)

Chaque environnement pose ses propres enregistrements : il n'y a pas de stack DNS centrale qui
devrait connaître à l'avance tous les hôtes. (Les previews du homelab n'y écrivent pas : elles vivent
sous `*.pr.houseflow.cloud`, zone et certificat propres au homelab.)

Convention : **un seul label sous la zone** (le wildcard ne couvre qu'un niveau), donc
`api-preprod` et non `api.preprod`. Pour chaque hôte d'API, un CNAME vers le FQDN par défaut de la
Container App et un TXT `asuid.<hôte>` portant l'ID de vérification du CAE, qu'Azure exige avant
d'accepter le hostname. Pour chaque frontend, un CNAME vers la Static Web App.

L'enregistrement racine n'est jamais géré par Terraform : la redirection
`houseflow.cloud → www.houseflow.cloud` est une configuration OVH statique.

TTL de 3600 s sur un environnement permanent, 60 s sur un environnement à échéance — qui se recrée avec
une nouvelle Static Web App, donc un nouvel hôte par défaut.

La zone est le seul point de contention entre environnements : tout ce qui y écrit (aujourd'hui
`apply-prod-dns`, demain la preprod) partage le groupe de concurrence `ovh-dns-zone`, qui le sérialise. C'est pour que ce verrou ne couvre que les
écritures OVH, et pas le provisionnement Azure, que le déploiement d'un environnement est découpé en
trois racines appliquées dans l'ordre :

| Racine | Contenu | Verrou `ovh-dns-zone` |
|---|---|---|
| `environment` | tout Azure ; **calcule** les enregistrements (leurs cibles sont des attributs du CAE et de la Static Web App) | non |
| `dns` | **écrit** les enregistrements dans la zone OVH | oui — quelques secondes |
| `custom-domains` | attend la propagation (60 s), puis lie les domaines côté Azure (Container App avec le wildcard, Static Web App qui émet son certificat) | non |

L'ordre n'est pas négociable : les cibles DNS n'existent qu'après `environment`, et Azure ne lie un
domaine qu'une fois le DNS résolu publiquement. Un groupe de concurrence GitHub garde par défaut
(`queue: single`) au plus **un seul job en attente** : un troisième arrivant annule celui qui
attendait. Tant que le verrou couvrait un apply complet (~25 min à la création), les déploiements
poussés pendant ce temps finissaient annulés (#233, #235, #237, #163). Le groupe `ovh-dns-zone`
porte désormais `queue: max` (#252) : une vraie file, jusqu'à 100 jobs en attente, exécutés dans
l'ordre sans annulation — incompatible avec `cancel-in-progress: true`, jamais utilisé sur ces
jobs. Le verrou reste court par ailleurs, la file n'ayant d'intérêt que si l'attente elle-même
reste raisonnable.

## Racines Terraform

```
infrastructure/terraform/
  environment/          un environnement complet et autonome (Azure)
    instances/          prod.tfvars
  dns/                  ses enregistrements dans la zone OVH
  custom-domains/       ses domaines personnalisés côté Azure
  shared/               Key Vault, identités du certificat et des dumps, conteneur db-dumps
  modules/ovh-dns-zone/
```

`environment`, `dns` et `custom-domains` sont instanciées par `name` (aujourd'hui `prod`), chacune avec son state
(`environment-<nom>.tfstate`, `dns-<nom>.tfstate`, `custom-domains-<nom>.tfstate`) et le storage
account passé en `-backend-config`. `dns` et `custom-domains` ne prennent que `name` et ce storage
account : elles lisent tout le reste dans le state d'`environment` (`terraform_remote_state`).

`shared` ne contient plus ni serveur PostgreSQL, ni VNet, ni identités d'environnement. Son
resource group et son storage account sont créés au bootstrap, hors Terraform : le backend doit
exister avant le premier apply.

## Flux de déploiement

```
 merge main ──► build ──► apply-shared ──► certificat     pipeline.yml
                      ──► plan-prod       plan publié dans le résumé + artefact
                      ──► approbation     lecture du plan
                      ──► apply-prod      applique CE plan, pas un nouveau
                      ──► apply-prod-dns ──► apply-prod-custom-domains

 02:00 UTC ────► dump pseudonymisé de la prod             job-dbtools-dump

 PR ouverte/poussée ──► preview docker compose sur le homelab   preview.yml (pas d'Azure)
 PR fermée ─────────► down + purge GHCR · filet : TTL           preview.yml · preview-reaper.yml
 (preprod Azure : à venir, #350)
```

**L'approbation arrive après le plan, et c'est l'essentiel.** Un environnement créé depuis zéro
prouve que le code produit une infrastructure qui fonctionne, jamais que ce même code appliqué à
l'état existant de la production est inoffensif. Un `replace` du serveur PostgreSQL n'apparaît que
dans un plan contre la prod. L'apply consomme le fichier de plan approuvé : si l'état a bougé
entre-temps, Terraform refuse plutôt que d'appliquer autre chose que ce qui a été lu.

`apply-shared` n'est pas derrière l'approbation — il doit tourner avant pour que le plan de prod
soit calculable — mais son garde-fou (`scripts/ci/tf-plan-guard.sh`) rejette toute destruction de
ressource protégée.

Il n'y a plus de destruction d'environnement par tag (ancien reaper et `destroy-environment.sh`) : plus
aucun environnement Azure n'est créé par une PR. La preprod (#350) reconstruira, depuis l'historique git,
ce dont elle a besoin.

## Bootstrap (manuel, une fois — détail dans `docs/azure-setup-guide.md`)

1. Deux souscriptions : production et environnements jetables.
2. Dans chacune, un resource group partagé nommé d'après elle (`rg-houseflow-shared-prod` ou
   `rg-houseflow-shared-ephemeral`), un storage account de states (nom globalement unique) et son
   conteneur `tfstate`. Ils doivent préexister à tout apply.
3. Rôles custom déployés et assignés : `HouseFlow Deployer` au scope souscription.
4. App registrations GitHub + federated credentials — **la casse du `subject` est significative**
   (`repo:BarbeRouss/HouseFlow:environment:prod`).
5. `id-houseflow-cert-prod`/`id-houseflow-cert-ephemeral` dans chaque souscription, et pour celle
   des jetables, `Key Vault Secrets User` sur le secret du certificat dans le Key Vault de
   production (attribution inter-souscriptions, faite une fois).
6. Environnements GitHub `prod`, `prod-approval` (required reviewers), `preview-homelab` (previews du
   homelab, `deploy/preview/README.md`) et `preview` (souscription jetable : plus utilisé par aucun
   workflow depuis #353, à réactiver pour la preprod #350). Secrets d'environnement `AZURE_CLIENT_ID`,
   `AZURE_SUBSCRIPTION_ID`, `JWT_KEY`, `BASTION_SSH_PUBLIC_KEY`, `TFSTATE_STORAGE_ACCOUNT` ;
   secrets de dépôt `AZURE_TENANT_ID`, `GHCR_PAT`, `OVH_APPLICATION_SECRET`, `OVH_CONSUMER_KEY`,
   `ENTRA_ADMIN_OBJECT_ID`, `ENTRA_ADMIN_NAME` ; variables `OVH_APPLICATION_KEY`,
   `LETSENCRYPT_EMAIL`, `KEY_VAULT_NAME`.
7. Avant un premier apply après une suppression : `az keyvault purge --name kv-houseflow`
   (soft-delete de 7 jours) — ou mieux, `az keyvault recover`, qui rend le vault **et** son
   certificat sans consommer le quota Let's Encrypt.
