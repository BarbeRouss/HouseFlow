# Previews de PR sur le homelab

Chaque PR qui doit être validée par product est déployée comme un **projet docker compose autonome**,
`pr-<n>`, sur la LXC `houseflow-preview` du homelab (arm64). Conception et décisions : issue #348 ;
cette issue-ci (#351) livre la pile et le script, le workflow GitHub qui les pilote est #352.

```
client ─ HTTPS ─▶ Traefik du homelab ─ HTTP ─▶ Traefik de la LXC ─▶ web   (<n>.pr.houseflow.cloud)
                  TLS *.pr.houseflow.cloud     provider Docker       api   (api-<n>.pr.houseflow.cloud)
                  middleware geoblock-be       réseau « previews »   postgres (réseau du projet seulement)
```

- Le Traefik du homelab termine le TLS (certificat wildcard), filtre (`geoblock-be`) et transmet en HTTP avec
  `X-Forwarded-Proto: https`.
- Le Traefik de la LXC lit les labels `Host(...)` que **chaque projet compose pose lui-même** : aucun fichier de
  routage à éditer, aucun port publié sur l'hôte (`ports:` n'existe nulle part dans `compose.yaml`).
- `web` et `api` sont sur le réseau Docker externe `previews` ; `postgres` ne l'est pas, il n'est joignable que
  depuis son projet.

## Prérequis de la LXC (posés une fois, hors dépôt)

- Docker (Compose v2), `flock`, `od` (coreutils) ; un utilisateur non root du groupe `docker`. Pas d'Azure CLI
  ni de `git` nécessaires à ce script.
- Un réseau Docker externe : `docker network create previews` (le nom est réglable, `PREVIEW_NETWORK`).
- Un Traefik v3 sur ce réseau : provider Docker avec `exposedByDefault=false`, entrée `web` (`:80`),
  `entryPoints.web.forwardedHeaders.trustedIPs` = adresse du Traefik du homelab (pour qu'il garde
  `X-Forwarded-For` / `-Proto` du premier proxy). **Traefik ≥ 3.6** avec Docker 29 : les versions antérieures
  échouent (« client version 1.24 is too old »).
- L'adresse du Traefik du homelab dans `PREVIEW_HOMELAB_PROXY` (voir *Réglages*) : sans elle, l'API ne
  saurait pas qui est le client réel (tout le monde partagerait le même compteur de limitation de débit) et
  `preview.sh up` refuse de démarrer.
- Le DNS `*.pr.houseflow.cloud` et la route du homelab vers la LXC.

## Commandes

```bash
scripts/preview/preview.sh up [--no-pull] <n> [<dump>]   # déploie la PR <n>
scripts/preview/preview.sh down <n>                      # la supprime
scripts/preview/preview.sh ls                            # previews et date du dernier déploiement
scripts/preview/preview.sh reap <ttl_heures>             # supprime les plus vieilles, purge les images
```

**`up`** : tire les images `ghcr.io/barberouss/houseflow-preview-{api,web,dbtools}:pr-<n>` (publiques, aucun
identifiant, **aucun `docker build`**), démarre le projet et attend la santé de l'API. Au démarrage :
`postgres` sain → `restore` (dump) → `migrate` (migrations de la branche, appliquées sur les données
restaurées) → `api` → santé. Idempotent : un `up` suivant tire les nouvelles images et redéploie, **sans
re-restaurer** (marqueur `__dbtools_restore` de dbtools) ni changer les secrets ; les migrations sont
rejouées. Sans `<dump>`, une base jamais restaurée garde les données de démo (`DEMO_MODE`). Le dump est
copié dans l'état du projet, supprimé par dbtools après la restauration (succès ou échec) puis par le
script ; le fichier de l'appelant n'est pas touché, c'est à lui de le supprimer.

**`down`** : `compose down -v` (conteneurs, volume, réseau du projet), suppression des images `pr-<n>` et du
répertoire d'état. Idempotent.

**`reap`** : `down` de toute preview dont le dernier déploiement dépasse le TTL (la politique est 7 jours, soit
`reap 168`), puis suppression des images `houseflow-preview-*` qu'aucun conteneur n'utilise plus et des
images orphelines, pour que le disque ne grossisse pas.

### Réglages

Variable d'environnement, ou ligne `CLE=valeur` dans `$PREVIEW_ROOT/preview.conf`.

| Variable | Défaut | Rôle |
|---|---|---|
| `PREVIEW_ROOT` | `$HOME/previews` | racine de l'état (`pr-<n>/`) |
| `PREVIEW_HOMELAB_PROXY` | — (obligatoire pour `up`) | adresse du Traefik du homelab |
| `PREVIEW_NETWORK` | `previews` | réseau Docker externe du Traefik de la LXC |
| `PREVIEW_DOMAIN` | `pr.houseflow.cloud` | zone des previews |
| `IMAGE_TAG` | `pr-<n>` | tag des images |

## Où vivent les secrets

`$PREVIEW_ROOT/pr-<n>/.env`, mode 0600, **jamais dans le dépôt** : le mot de passe PostgreSQL et la clé de
signature JWT, générés (`/dev/urandom`) au premier `up`, réutilisés ensuite, supprimés au `down`. Le script
ne les affiche ni ne les journalise ; ils ne passent pas par la ligne de commande. Il n'y en a pas d'autre :
les images ne contiennent aucun secret, ce qui permet de les publier.

## Configuration de l'API

Alignée sur une preview Azure (`infrastructure/terraform/environment/apps.tf`) : `Staging`, `DEMO_MODE=true`,
`CORS__ORIGINS=https://<n>.pr.houseflow.cloud`, émetteur et ressource OAuth/MCP en
`https://api-<n>.pr.houseflow.cloud`, JWT `HouseFlow-pr-<n>` (comme Azure). Différences, voulues :

- **PostgreSQL par mot de passe et sans TLS** (`SSL Mode=Disable`) : réseau interne du projet, données
  pseudonymisées, durée bornée. La prod et la preprod gardent TLS + Entra.
- **Proxys de confiance** (`HttpEdge__TrustedProxyNetworks__0`, `HttpEdge__ForwardLimit=2`) : le sous-réseau du
  réseau `previews` (d'où l'API voit le Traefik de la LXC) et le Traefik du homelab ; deux sauts de
  `X-Forwarded-For` consommés. Ce qu'un client a lui-même mis dans l'en-tête, plus à gauche, n'est jamais
  cru. Le sous-réseau est lu sur le réseau à chaque `up`.

Limites mémoire (`mem_limit`) : api 512 Mio, postgres 384 Mio, web 128 Mio ; `restore` 256 Mio et `migrate`
512 Mio ne tournent qu'au démarrage, avant l'API. Mesures au repos sur la pile démarrée (amd64, avant
usage) : api ≈ 94 Mio, postgres ≈ 56 Mio, web ≈ 60 Mio. Ordre de grandeur : ≈ 1 Gio réservé par preview, 3
previews = 3 Gio sur les 6 Gio de la LXC.

## Architecture arm64 et essai à la main

Les images arm64 sont construites par le workflow (#352) sur un runner hébergé `ubuntu-24.04-arm`, puis
tirées par la LXC. Les `FROM` des Dockerfiles et celui de `compose.yaml` sont épinglés par digest
**d'index multi-architecture** (arm64 inclus, vérifié avec `docker buildx imagetools inspect`).

Avant le workflow, ou en repli si les runners ARM étaient indisponibles : construire sur la LXC (arm64 natif)
sous les mêmes noms, puis déployer sans tirer :

```bash
R=ghcr.io/barberouss/houseflow-preview; N=<n>
docker build -f src/HouseFlow.API/Dockerfile       -t $R-api:pr-$N .
docker build -f src/HouseFlow.WebHost/Dockerfile   -t $R-web:pr-$N .
docker build -f dbtools/Dockerfile                 -t $R-dbtools:pr-$N dbtools
PREVIEW_HOMELAB_PROXY=<adresse> scripts/preview/preview.sh up --no-pull $N [<dump>]
```

`--no-pull` garde les images déjà présentes et ne tire que celles qui manquent (l'image PostgreSQL, la
première fois). Un build complet est lourd pour la LXC (CPU lent, 6 Gio partagés) : c'est un repli, pas
le chemin normal.

## Vérifier une preview

```bash
docker compose -p pr-<n> ps                           # api « healthy », restore et migrate « Exited (0) »
docker compose -p pr-<n> logs restore                 # « Importé : … » (jamais le mot de passe)
for c in $(docker ps -q --filter label=com.docker.compose.project=pr-<n>); do docker port "$c"; done   # vide
curl -sI https://api-<n>.pr.houseflow.cloud/health    # 200, pas de redirection HTTP→HTTPS en boucle
```

Le cookie de refresh est `Secure`, et la table `AuditLogs` d'une connexion porte l'IP du client, pas celle
d'un proxy.
