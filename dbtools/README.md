# `dbtools`

Image d'outillage base de données, publiée par le job `build` de `pipeline.yml` sur
`ghcr.io/barberouss/houseflow-dbtools:<tag>`. Elle ne s'exécute que comme **Container Apps Job**
dans un CAE peeré avec le VNet du serveur : le runner GitHub n'a aucun chemin réseau vers
`psql-houseflow`, tout le SQL d'administration passe par ici.

L'authentification est **exclusivement** par identité managée. Le script demande un token Entra
(`https://ossrdbms-aad.database.windows.net`) à l'endpoint d'identité injecté par Container Apps,
puis le présente à PostgreSQL comme mot de passe. Aucun mot de passe n'existe, nulle part.

## Sous-commandes

    dbtools <roles|init|dump|restore>

La sous-commande est un **argument** (`args` de la définition du job Terraform), pas une variable
d'environnement : il n'y a pas de `COMMAND`, et pas de `CMD` par défaut dans l'image.

| Commande  | Job                 | CAE     | Identité               | Effet |
|-----------|---------------------|---------|------------------------|-------|
| `roles`   | `job-dbtools-roles` | prod    | `id-houseflow-prod`    | crée les principaux Entra de `MANAGED_ROLES`, la base `houseflow_preprod` (owner `id-houseflow-preprod`), et donne `CREATEDB` à `id-houseflow-preview` |
| `init`    | `job-dbtools-init`  | preview | `id-houseflow-preview` | crée (`ACTION=create`) ou supprime (`ACTION=drop`) `houseflow_pr_<PR_NUMBER>` |
| `dump`    | —                   | prod    | `id-houseflow-prod`    | non implémenté — issue #199 |
| `restore` | —                   | preprod / preview | `id-houseflow-<env>` | non implémenté — issue #199 |

Toutes sont idempotentes : les rejouer ne change rien.

## Variables d'environnement

| Variable | Obligatoire | Défaut | Rôle |
|---|---|---|---|
| `PG_HOST` | oui | — | FQDN privé du serveur (`houseflow.private.postgres.database.azure.com`) |
| `PG_USER` | oui | — | nom de l'identité managée, qui est aussi le nom du rôle PostgreSQL |
| `AZURE_CLIENT_ID` | oui | — | client id de cette identité (sélectionne l'identité sur l'endpoint) |
| `IDENTITY_ENDPOINT`, `IDENTITY_HEADER` | oui | — | injectés automatiquement par Container Apps |
| `ADMIN_DB` | non | `postgres` | base sur laquelle les ordres `CREATE`/`DROP DATABASE` sont émis |
| `PR_NUMBER` | `init` | — | numéro de la PR, entier |
| `ACTION` | non | `create` | `init` : `create` ou `drop` |
| `MANAGED_ROLES` | non | `id-houseflow-preprod,id-houseflow-preview` | `roles` : identités à déclarer comme principaux Entra |
| `PREPROD_ROLE`, `PREPROD_DB`, `PREVIEW_ROLE` | non | `id-houseflow-preprod`, `houseflow_preprod`, `id-houseflow-preview` | `roles` : noms utilisés pour la base preprod et l'attribut `CREATEDB` |

Les identifiants sont validés (`^[A-Za-z0-9_-]+$`) avant d'entrer dans du SQL, et `init` refuse
tout nom de base qui n'est pas `houseflow_pr_<n>` — l'identité preview a `CREATEDB` et ne doit
jamais toucher `houseflow_prod` ni `houseflow_preprod`.

## Lancer un job

Le pipeline passe par `scripts/ci/run-dbtools-job.sh <job> <resource-group> [VAR=VALEUR …]`, qui
démarre le job **et attend la fin de l'exécution** (une exécution en échec fait échouer l'étape) :

    bash scripts/ci/run-dbtools-job.sh job-dbtools-init rg-houseflow-preview PR_NUMBER=123 ACTION=create
    bash scripts/ci/run-dbtools-job.sh job-dbtools-roles rg-houseflow-prod

À la main, sans l'attente :

    az containerapp job start --name job-dbtools-init -g rg-houseflow-preview \
      --env-vars PR_NUMBER=123 ACTION=create
    az containerapp job execution list -n job-dbtools-init -g rg-houseflow-preview \
      --query "[0].{name:name,status:properties.status}" -o table
    az containerapp job logs show -n job-dbtools-init -g rg-houseflow-preview \
      --execution <nom-de-l-exécution>

## Pourquoi l'image ne contient pas `azure-cli`

`azure-cli` n'est packagé dans aucun dépôt Alpine (3.24 main/community, edge/community,
edge/testing). La seule voie est `pip install azure-cli`, qui fonctionne sans compilateur mais
ajoute ~713 Mo — une image dix fois plus grosse, reconstruite à chaque push touchant `dbtools/`.

Aucune des sous-commandes implémentées n'en a besoin : `roles` et `init` obtiennent leur token
directement sur l'endpoint d'identité, en HTTP. `az` ne servirait qu'à `dump`/`restore` pour le
conteneur blob `db-dumps`, qui relèvent de l'issue #199 — c'est elle qui tranchera entre installer
le CLI et attaquer l'API REST Storage avec un token obtenu de la même façon que celui de
PostgreSQL. Le `Dockerfile` porte la ligne `pip` prête à décommenter.
