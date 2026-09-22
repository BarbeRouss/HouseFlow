# Image d'outillage base de données de HouseFlow : `dbtools dump` et `dbtools restore`.
#
# Elle tourne exclusivement comme Container Apps Job dans le CAE d'un environnement :
# c'est le seul chemin réseau vers son serveur PostgreSQL privé (le runner GitHub n'en a
# aucun). Elle embarque psql / pg_dump / pg_restore, et de quoi échanger les identités
# managées du job contre des tokens Entra (PostgreSQL et Storage).
#
# pg_dump refuse un serveur plus récent que lui : une montée de version majeure du serveur
# de prod impose de monter cette base d'image avec.
#
# Base épinglée par digest : postgres:16-alpine, Alpine 3.24.2.
FROM postgres:16-alpine@sha256:3c5c8892d184f738f4fe282d14ddaa613a38f00f4189d2d94725ebe6f2909ddb

# bash : le script utilise `set -o pipefail`, les tableaux et `[[ =~ ]]`
# curl : endpoint d'identité des Container Apps, et API REST du Blob Storage
# jq   : extraction de `.access_token`
#
# Pas d'azure-cli : il n'est packagé dans aucun dépôt Alpine, et `pip install azure-cli`
# ajoute ~700 Mo pour deux requêtes HTTP qu'un token et curl font aussi bien.
RUN apk add --no-cache bash curl jq

COPY pseudonymize.sql verify.sql /usr/local/share/dbtools/
COPY dbtools /usr/local/bin/dbtools
RUN chmod 0755 /usr/local/bin/dbtools

# La sous-commande est passée en `args` par la définition du job Terraform (["dump"],
# ["restore"]) ; pas de CMD par défaut, pour qu'un job mal défini échoue sur l'usage
# plutôt que d'agir au hasard.
ENTRYPOINT ["/usr/local/bin/dbtools"]
