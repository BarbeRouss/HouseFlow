# Image d'outillage base de données de HouseFlow.
#
# Elle tourne exclusivement comme Container Apps Job dans un CAE peeré avec le
# VNet du serveur PostgreSQL : c'est le seul chemin réseau vers psql-houseflow
# (le runner GitHub n'en a aucun). Elle embarque psql / pg_dump (base postgres)
# et de quoi échanger l'identité managée du job contre un token Entra.
#
# Base épinglée par digest : postgres:16-alpine, Alpine 3.24.2.
FROM postgres:16-alpine@sha256:3c5c8892d184f738f4fe282d14ddaa613a38f00f4189d2d94725ebe6f2909ddb

# bash : le script utilise `set -o pipefail`, les tableaux et `[[ =~ ]]`
# curl : appel de l'endpoint d'identité des Container Apps (IDENTITY_ENDPOINT)
# jq   : extraction de `.access_token` de sa réponse
RUN apk add --no-cache bash curl jq

# ─── Pourquoi pas azure-cli ? ────────────────────────────────────────────────
# `azure-cli` n'est packagé dans AUCUN dépôt Alpine (ni 3.24 main/community, ni
# edge/community, ni edge/testing — vérifié). La seule voie est
# `apk add py3-pip && pip install azure-cli` : elle fonctionne sans compilateur
# (wheels musllinux) mais ajoute ~713 Mo de site-packages, soit une image ~10x
# plus grosse, reconstruite à chaque push touchant dbtools/.
#
# Or aucune des sous-commandes implémentées n'appelle `az` : `roles` et `init`
# obtiennent leur token PostgreSQL directement sur l'endpoint d'identité en
# HTTP. `az` ne servirait qu'à `dump` / `restore` (accès au conteneur blob
# db-dumps), qui sont des stubs jusqu'à l'issue #199. Cette issue tranchera
# entre ajouter la ligne ci-dessous et attaquer l'API REST Storage avec un
# token obtenu exactement de la même façon que celui de PostgreSQL :
#
#   RUN apk add --no-cache py3-pip \
#    && pip install --no-cache-dir --break-system-packages --only-binary=:all: azure-cli
# ─────────────────────────────────────────────────────────────────────────────

COPY dbtools /usr/local/bin/dbtools
RUN chmod 0755 /usr/local/bin/dbtools

# La sous-commande est passée en `args` par la définition du job Terraform
# (["roles"], ["init"], …) ; il n'y a délibérément pas de CMD par défaut pour
# qu'un job mal défini échoue sur l'usage plutôt que d'agir au hasard.
ENTRYPOINT ["/usr/local/bin/dbtools"]
