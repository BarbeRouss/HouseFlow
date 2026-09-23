# `dbtools`

Image d'outillage base de données : elle produit chaque nuit un dump **pseudonymisé** de la prod,
et le restaure dans chaque environnement de PR à sa création. Elle ne s'exécute que comme
**Container Apps Job**, dans le CAE de l'environnement — seul chemin réseau vers son serveur
PostgreSQL privé, que le runner GitHub n'a pas.

```
prod     job-dbtools-dump      cron 02:00 UTC
         pg_dump houseflow_prod (sans le schéma hangfire) → base de travail houseflow_dumpwork
         → pseudonymize.sql → verify.sql (une violation = rien n'est publié)
         → pg_dump -Fc → db-dumps/latest.dump

pr-<n>   job-dbtools-restore   lancé par pr-preview.yml après chaque terraform apply
         db-dumps/latest.dump → houseflow_pr_<n>, en une transaction, une seule fois
         → pr-preview.yml redémarre l'API : son init container applique les migrations de la branche
```

L'authentification est **exclusivement** par identité managée : le script demande des tokens
Entra à l'endpoint d'identité injecté par Container Apps — `ossrdbms-aad` pour PostgreSQL, où le
token tient lieu de mot de passe, et `storage.azure.com` pour l'API REST du Blob Storage. Aucun
mot de passe ni clé de compte n'existe nulle part.

## Qui a le droit de quoi

Chaque job porte deux identités :

| Identité | Sert à | Droit |
|---|---|---|
| `id-houseflow-<nom>` (celle de l'environnement) | PostgreSQL | administratrice Entra de **son** serveur, et d'aucun autre |
| `id-houseflow-dumps-writer` (prod) ou `id-houseflow-dumps-reader` (PR), dans le resource group permanent de la souscription | blob `db-dumps` | **Contributor** pour `-writer`, **Reader** pour `-reader` |

Le nom annonce le droit, mais c'est la souscription qui le garantit : la souscription jetable ne
contient qu'`id-houseflow-dumps-reader`. Une PR qui modifierait ce code ne pourrait donc ni
écraser le dump (aucune identité de sa souscription ne peut écrire), ni atteindre la base de prod
(autre souscription, autre VNet, autre administrateur). Détail : `infrastructure/rbac/README.md`.

## Ce qui est pseudonymisé

`pseudonymize.sql` remplace tout ce qui identifie une personne, sauf pour les comptes de
`PRESERVED_EMAILS` (`preserved_emails` dans `instances/prod.tfvars`), qui restent intacts avec
leurs maisons et tout ce qu'elles contiennent. La correspondance ignore la casse.

| Table | Traitement |
|---|---|
| `Users` | e-mail `user-<id>@pseudonymise.invalid`, prénom et nom génériques, hash BCrypt valide qu'aucun mot de passe ne vérifie |
| `RefreshTokens` | tous supprimés, comptes préservés compris |
| `ApiKeys` | supprimées, sauf celles des comptes préservés |
| `AuditLogs` | utilisateur, IP, user-agent, valeurs avant/après et données annexes vidés ; l'action, l'entité et la date restent |
| `Invitations` | jeton remplacé |
| `Houses` | nom, adresse, code postal, ville remplacés ; le pays reste |
| `MaintenanceInstances` | prestataire et notes (texte libre) remplacés |

Le schéma `hangfire` n'est pas copié : les jobs de la prod ne doivent pas se rejouer dans une
preview, et leurs arguments peuvent contenir des données personnelles.

`verify.sql` contrôle ensuite le résultat contre les valeurs **attendues** — et non contre les
valeurs d'origine — et le dump échoue sans rien publier au moindre écart. Le test
`PseudonymizationTests` exécute les deux fichiers sur une base migrée par EF, et échoue si une
colonne texte est ajoutée au modèle sans être classée : **toute nouvelle colonne pouvant porter
une donnée personnelle doit être traitée dans `pseudonymize.sql` et contrôlée dans `verify.sql`.**

## Restauration

La base d'une PR est vidée et restaurée **dans une seule transaction** : l'API ne voit jamais un
état intermédiaire, et un `pg_restore` qui échoue ne laisse rien derrière lui. Tout ce que porte
le schéma `public` appartient à l'identité de l'environnement (c'est aussi celle de l'API) : le
job le supprime, y compris les tables d'une migration propre à la branche, que l'init container
`--migrate` recrée au redémarrage de l'API sur les données restaurées. C'est ce qui fait valider
les migrations de la PR sur des données de prod.

Une table `__dbtools_restore` marque la base restaurée : les pushes suivants de la PR relancent
le job, qui rend la main aussitôt, et les données modifiées pendant la revue restent en place.

Sans `latest.dump` (avant la première nuit), le job avertit et réussit : l'environnement garde
ses données de démo.

## Variables d'environnement

Posées par la définition du job (`infrastructure/terraform/environment/dbtools.tf`).

| Variable | Obligatoire | Rôle |
|---|---|---|
| `PG_HOST` | oui | FQDN privé du serveur de l'environnement |
| `PG_USER` | oui | nom de l'identité de l'environnement, qui est aussi son rôle PostgreSQL |
| `PG_DATABASE` | oui | base de l'environnement (`houseflow_prod`, `houseflow_pr_<n>`) — `restore` refuse `houseflow_prod` |
| `AZURE_CLIENT_ID` | oui | client id de l'identité de l'environnement |
| `DUMPS_CLIENT_ID` | oui | client id d'`id-houseflow-dumps-writer` ou d'`id-houseflow-dumps-reader` |
| `DUMPS_STORAGE_ACCOUNT` | oui | storage account qui porte `db-dumps` (celui des states de la production) |
| `PRESERVED_EMAILS` | `dump` | comptes laissés intacts, séparés par des virgules |
| `FORCE` | non | `restore` : `true` pour restaurer une base déjà restaurée |
| `IDENTITY_ENDPOINT`, `IDENTITY_HEADER` | oui | injectés par Container Apps |

La sous-commande est un **argument** (`args` du job), pas une variable : l'image n'a pas de
`CMD`, pour qu'un job mal défini échoue sur l'usage plutôt que d'agir au hasard.

## Opérations

Les commandes `az containerapp job` demandent l'extension `containerapp`
(`az extension add --name containerapp`).

**Lancer un dump hors du cron** — par exemple juste après le premier déploiement, pour ne pas
attendre la nuit :

    az containerapp job start -n job-dbtools-dump -g rg-houseflow-prod

**Restaurer à nouveau une preview** (données de la PR écrasées), puis redémarrer son API pour
rejouer les migrations de la branche :

    bash scripts/ci/run-dbtools-job.sh job-dbtools-restore rg-houseflow-pr-<n> FORCE=true
    az containerapp revision restart -n ca-api-pr-<n> -g rg-houseflow-pr-<n> \
      --revision "$(az containerapp show -n ca-api-pr-<n> -g rg-houseflow-pr-<n> --query properties.latestRevisionName -o tsv)"

`run-dbtools-job.sh` démarre le job et attend la fin de l'exécution ; une exécution en échec le
fait échouer. C'est lui que `pr-preview.yml` appelle. Avec `DBTOOLS_LOG_WORKSPACE` (le customer id
du workspace Log Analytics, sortie Terraform `log_analytics_workspace_id`), il recopie ensuite les
lignes du job dans le log du workflow, en attendant leur ingestion, qui prend une à quelques
minutes : l'étape « Restaurer les données de prod pseudonymisées » d'une preview montre ainsi la
ligne `Importé : …`.

**Lire les logs d'une exécution** :

    az containerapp job execution list -n job-dbtools-dump -g rg-houseflow-prod \
      --query "[].{nom:name, statut:properties.status, début:properties.startTime}" -o table
    az containerapp job logs show -n job-dbtools-dump -g rg-houseflow-prod --execution <nom>

**Dump rejeté par la vérification** : les logs listent chaque contrôle en échec et son nombre de
lignes. La base `houseflow_dumpwork` est laissée sur le serveur de prod pour l'enquête (via le
bastion) ; elle ne contient rien que la prod n'ait déjà, et le dump suivant la recrée.

## Limites

- **Version PostgreSQL** : l'image est en `postgres:16` et `pg_dump` refuse un serveur plus
  récent que lui. Monter le serveur de prod de version majeure impose de monter l'image avec.
- **Un seul dump** : `latest.dump` est remplacé chaque nuit (un PUT de blob est atomique). Il n'y
  a pas d'historique — ce n'est pas une sauvegarde, le PITR du serveur en est une.
