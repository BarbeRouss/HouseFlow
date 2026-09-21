# Infrastructure HouseFlow — cible

Ce document décrit l'infrastructure Azure et le flux de déploiement (le QUOI). Il ne porte
aucun statut d'avancement. Domaine : `houseflow.cloud`. Région : `westeurope`. Préfixe : `houseflow`.

## Principes

1. **Un environnement possède tout ce dont il dépend** — son resource group, son réseau, son
   serveur PostgreSQL, son Container Apps Environment, son identité. Rien de ce qui peut être
   dupliqué n'est partagé. C'est ce qui rend un changement d'infrastructure éprouvable : une
   montée de version majeure de PostgreSQL, un changement de SKU ou de subnet s'applique sur un
   environnement jetable, jamais sur celui qui porte la production faute d'autre cible.
2. **La production n'est pas un cas particulier du code.** C'est l'instance de la racine
   Terraform dont l'échéance est vide. Un environnement de pull request, c'est le même
   `terraform apply` avec un autre `name` — d'où le fait qu'une PR valide ce qui touchera la prod.
3. **Ce qui reste partagé n'est ni du compute ni de la donnée** : le certificat wildcard (Let's
   Encrypt plafonne les certificats identiques à 5 par semaine, donc un environnement jetable ne
   peut pas émettre le sien), la zone DNS, le storage des states.
4. **La destruction se fait par tag, jamais par state.** Un `terraform destroy` exige un state
   sain ; or c'est précisément quand le state est perdu ou corrompu qu'un environnement devient
   un orphelin facturé. Le reaper lit les tags Azure et appelle `az group delete`.
5. **Pas de `terraform_remote_state`** : les références croisées passent par des data sources sur
   des noms fixes. Un service principal ne lit que ses propres states.

## Les deux formes d'environnement

```
 PERMANENT (1)                        JETABLE (N)
 ──────────────────────────           ────────────────────────────────────────
 rg-houseflow-prod                    rg-houseflow-<nom>   tags: ttl · environment
   vnet-prod                            vnet-<nom>
   psql-houseflow-prod                  psql-houseflow-<nom>
   cae-prod · log-prod                  cae-<nom> · log-<nom>
   id-prod · ca-bastion-prod            id-<nom>
   ca-api-prod · swa-prod               ca-api-<nom> · swa-<nom>
   houseflow_prod                       houseflow_<nom>
   www · api                            <nom> · api-<nom>
   🔒 lock, aucun tag ttl

            ┌── PARTAGÉ — ni compute ni donnée ─────────────────┐
            │  kv-houseflow   certificat *.houseflow.cloud      │
            │                 + compte ACME                     │
            │  id-houseflow-cert  seule identité habilitée à    │
            │                 lire le secret du certificat      │
            │  st…tfstate     les states                        │
            │                 + conteneur db-dumps              │
            │  zone OVH       houseflow.cloud                   │
            └───────────────────────────────────────────────────┘
```

Les instances jetables portent un tag `ttl` (échéance RFC3339, quatre heures repoussées à chaque
événement de la PR) et `environment` (leur nom). La
production n'a pas de tag `ttl` : c'est sa seule protection côté reaper, et elle suffit — aucune
liste d'exclusion à maintenir le jour où un environnement nommé apparaît.

Chaque environnement a son propre VNet et aucun peering n'existe entre eux : deux instances
peuvent donc porter le même plan d'adressage (`10.0.0.0/16`, `snet-db` en `/28`, `snet-cae` en
`/23`) sans se gêner, et il n'y a pas de registre de plages à tenir.

## Instances

Il n'y en a que deux.

| instance | échéance | lock | bastion | charge API | hôtes DNS |
|---|---|---|---|---|---|
| `prod` | ∅ (permanente) | ✓ | ✓ | 1 réplica minimum | `www`, `api` |
| `pr-<n>` | 4 h glissantes | ✗ | ✗ | scale-to-zero | `pr-<n>`, `api-pr-<n>` |

Il n'existe **pas d'environnement de validation séparé**, et c'est délibéré : celui d'une pull
request est déjà complet, donc il éprouve un changement d'infrastructure dans la PR même qui
l'introduit. Un preprod ferait doublon, avec le défaut supplémentaire de reposer sur la
discipline — penser à le lancer — là où la PR le fait d'elle-même.

Les colonnes « lock » et « charge API » ne sont pas des réglages : elles se déduisent de
l'échéance (`main.tf`, locals). En faire des variables rendait représentable l'environnement
éphémère **et** verrouillé, c'est-à-dire un resource group promis au reaper qu'il ne peut pas
détruire — la fuite d'argent que tout ce design écarte. Un état qu'on ne peut pas écrire est un
état qu'on ne peut pas atteindre par erreur.

Il ne reste donc dans `instances/pr.tfvars` que deux lignes, `bastion_enabled` et `demo_mode` ;
tout le reste du code est commun à la production.

Les jeux de variables sont versionnés dans `infrastructure/terraform/environment/instances/` :
ils décrivent des environnements, pas des secrets, et sans eux la production ne serait pas
reproductible.

## Souscriptions

La production et les environnements jetables vivent dans des souscriptions distinctes. C'est une
frontière qu'aucun tag mal posé ni aucun bug de filtre ne peut franchir : le service principal
qui crée et détruit les environnements jetables n'a aucun rôle dans la souscription de
production.

Chaque souscription a son propre `rg-houseflow-shared`, portant son storage de states (les noms
de storage account sont uniques au niveau mondial) et son identité `id-houseflow-cert`. Aucun
stack ne lit le state d'un autre, donc un storage central ne rendrait service à personne.

Le certificat wildcard est le seul lien entre les deux souscriptions, et il est irréductible. Il
vit dans le Key Vault de la souscription de **production** ; l'identité de certificat de la
souscription jetable y reçoit `Key Vault Secrets User` sur le secret seul, attribué une fois au
bootstrap. Le sens de la dépendance compte : le jetable lit le permanent, jamais l'inverse.

## Identités et RBAC

Trois identités par environnement au plus, et **une seule attribution de rôle dans toute
l'infrastructure**.

| identité | portée | rôle |
|---|---|---|
| `id-houseflow-<nom>` | resource group de l'environnement | administratrice Entra de **son** serveur PostgreSQL, et d'aucun autre. Aucun rôle RBAC Azure. |
| `id-houseflow-cert` | `rg-houseflow-shared` de sa souscription | `Key Vault Secrets User` sur le secret du certificat. Attachée à chaque CAE pour sa référence Key Vault. |
| service principal GitHub | souscription | `HouseFlow Deployer` |

C'est cette séparation qui rend un environnement éphémère créable sans droit d'attribution de
rôle. Si l'identité de l'environnement devait lire le Key Vault, il faudrait lui attribuer un
rôle **à chaque création** — donc confier au service principal de déploiement le pouvoir de
distribuer des rôles, ce que `HouseFlow Deployer` n'accorde pas délibérément.

Le rôle `HouseFlow Deployer` est assignable au **scope souscription** (un environnement éphémère
crée son propre resource group, dont le nom n'est pas connu à l'avance) et porte
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

Chaque environnement pose ses propres enregistrements : il n'y a plus de stack DNS centrale qui
devrait connaître à l'avance tous les hôtes.

Convention : **un seul label sous la zone** (le wildcard ne couvre qu'un niveau), donc
`api-pr-42` et non `api.pr-42`. Pour chaque hôte d'API, un CNAME vers le FQDN par défaut de la
Container App et un TXT `asuid.<hôte>` portant l'ID de vérification du CAE, qu'Azure exige avant
d'accepter le hostname. Pour chaque frontend, un CNAME vers la Static Web App.

L'enregistrement racine n'est jamais géré par Terraform : la redirection
`houseflow.cloud → www.houseflow.cloud` est une configuration OVH statique.

TTL de 3600 s sur un environnement permanent, 60 s sur un jetable — qui se recrée avec une
nouvelle Static Web App, donc un nouvel hôte par défaut.

La zone est le seul point de contention entre environnements : tous les applies partagent le
groupe de concurrence `ovh-dns-zone`, qui les sérialise.

## Racines Terraform

```
infrastructure/terraform/
  environment/          un environnement complet et autonome
    instances/          prod.tfvars · pr.tfvars
  shared/               Key Vault, identité du certificat, conteneur db-dumps
  modules/ovh-dns-zone/
```

`environment` est instanciée par `name`, avec son state propre
(`environment-<nom>.tfstate`) et le storage account passé en `-backend-config`.

`shared` ne contient plus ni serveur PostgreSQL, ni VNet, ni identités d'environnement. Son
resource group et son storage account sont créés au bootstrap, hors Terraform : le backend doit
exister avant le premier apply.

## Flux de déploiement

```
 PR ouverte ──► environnement COMPLET pr-<n>        pr-preview.yml
                ~25 min (psql ~10' + cae ~5' + DNS + bind du certificat)
 push, push ──► image API + wwwroot seulement       ~2 min
 PR fermée  ──► destroy · filet : tag ttl + reaper

 merge main ──► build ──► apply-shared ──► certificat     pipeline.yml
                      ──► plan-prod       plan publié dans le résumé + artefact
                      ──► approbation     lecture du plan
                      ──► apply-prod      applique CE plan, pas un nouveau

 horaire ──────► suppression des resource groups expirés  reaper.yml
```

**L'approbation arrive après le plan, et c'est l'essentiel.** Un environnement de PR est toujours
créé depuis zéro : il prouve que le code produit une infrastructure qui fonctionne, jamais que ce
même code appliqué à l'état existant de la production est inoffensif. Un `replace` du serveur
PostgreSQL n'apparaît que dans un plan contre la prod. L'apply consomme le fichier de plan
approuvé : si l'état a bougé entre-temps, Terraform refuse plutôt que d'appliquer autre chose que
ce qui a été lu.

`apply-shared` n'est pas derrière l'approbation — il doit tourner avant pour que le plan de prod
soit calculable — mais son garde-fou (`scripts/ci/tf-plan-guard.sh`) rejette toute destruction de
ressource protégée.

Le seul chemin pour éprouver un changement d'infrastructure est **d'ouvrir la PR**. Son
environnement est complet, créé par le code de la branche : ce qui y passe est ce qui passera en
production.

## Reaper

Horaire. Liste les resource groups portant un tag `ttl` **et** `project=houseflow`, compare
l'échéance à l'heure courante, et supprime ceux qui l'ont dépassée par `az group delete`, puis
leur blob de state. Un resource group sans tag `ttl` n'entre jamais dans la liste des candidats.

Il ne lit aucun state et n'appelle jamais Terraform : c'est ce qui lui permet de ramasser un
environnement dont l'apply s'est interrompu, ou dont le state a été perdu — le seul cas où un
environnement pourrait être facturé indéfiniment.

Il tourne dans la souscription des environnements jetables et n'a aucun chemin vers la production.

## Bootstrap (manuel, une fois — détail dans `docs/azure-setup-guide.md`)

1. Deux souscriptions : production et environnements jetables.
2. Dans chacune : `rg-houseflow-shared`, un storage account de states (nom globalement unique) et
   son conteneur `tfstate`. Ils doivent préexister à tout apply.
3. Rôles custom déployés et assignés : `HouseFlow Deployer` au scope souscription.
4. App registrations GitHub + federated credentials — **la casse du `subject` est significative**
   (`repo:BarbeRouss/HouseFlow:environment:prod`).
5. `id-houseflow-cert` dans chaque souscription, et pour celle des jetables, `Key Vault Secrets
   User` sur le secret du certificat dans le Key Vault de production (attribution
   inter-souscriptions, faite une fois).
6. Environnements GitHub `prod`, `preview` (limité à `main` sauf `preview`) et
   `prod-approval` (required reviewers). Secrets d'environnement `AZURE_CLIENT_ID`,
   `AZURE_SUBSCRIPTION_ID`, `JWT_KEY`, `BASTION_SSH_PUBLIC_KEY`, `TFSTATE_STORAGE_ACCOUNT` ;
   secrets de dépôt `AZURE_TENANT_ID`, `GHCR_PAT`, `OVH_APPLICATION_SECRET`, `OVH_CONSUMER_KEY`,
   `ENTRA_ADMIN_OBJECT_ID`, `ENTRA_ADMIN_NAME` ; variables `OVH_APPLICATION_KEY`,
   `LETSENCRYPT_EMAIL`, `KEY_VAULT_NAME`.
7. Avant un premier apply après une suppression : `az keyvault purge --name kv-houseflow`
   (soft-delete de 7 jours) — ou mieux, `az keyvault recover`, qui rend le vault **et** son
   certificat sans consommer le quota Let's Encrypt.
