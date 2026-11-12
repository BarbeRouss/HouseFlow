# Previews de PR sur le homelab

Chaque PR qui doit être validée par product est déployée comme un **projet docker compose autonome**,
`pr-<n>`, sur la LXC `houseflow-preview` du homelab (arm64). Conception et décisions : issue #348 ;
#351 a livré la pile et le script, #352 le workflow GitHub qui les pilote (`.github/workflows/preview.yml`,
`preview-reaper.yml`) : voir *Workflow GitHub* ci-dessous.

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

- Le runner GitHub, enregistré **au niveau du dépôt** avec le seul label `houseflow-preview`
  (`--no-default-labels` : ni `self-hosted`, ni `linux`, ni `ARM64`), d'où `runs-on: houseflow-preview`
  dans les workflows. Sous un utilisateur non root du groupe `docker`. Azure CLI (`az`), `jq`, `curl` et
  `git` pour le workflow ; pas de `gh`.
- `$PREVIEW_ROOT/preview.conf` (0600) avec `PREVIEW_HOMELAB_PROXY=<adresse du Traefik du homelab>` : le
  workflow ne la porte pas (une adresse privée n'a rien à faire dans un dépôt public).

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
usage) : api ≈ 94 Mio, postgres ≈ 56 Mio, web ≈ 60 Mio. Mesuré sur le pi4 : une preview au repos ≈ 200 Mio ; les
`mem_limit` d'une pile totalisent ≈ 1 Gio, soit 5 Gio au plus pour 5 previews sur les 6 Gio de la LXC (l'alerte
`PreviewHostMemoryHigh` se déclenche à 85 % de mémoire utilisée).

## Workflow GitHub

`.github/workflows/preview.yml`, déclenché par `pull_request` (`opened`, `synchronize`, `reopened`,
`closed`) et `workflow_dispatch` (numéro de PR ; `max_previews` pour essayer le plafond) :

| Job | Où | Rôle |
|---|---|---|
| `resolve` | hébergé | numéro, commit, éligibilité (même dépôt, hors Dependabot, PR ouverte pour un `workflow_dispatch`) |
| `build` | `ubuntu-24.04-arm` | 3 images `linux/arm64` natives, poussées sur `ghcr.io/barberouss/houseflow-preview-{api,web,dbtools}:pr-<n>` ; cache `type=gha` ; `provenance`/`sbom` désactivés |
| `deploy` | runner LXC (`houseflow-preview`) | plafond, login GHCR, dump (OIDC), `preview.sh up`, **vérification que le front et l'API répondent à travers le Traefik local** ; ne construit rien |
| `report` | hébergé | label `preview` et commentaire unique (marqueur `<!-- houseflow-preview-homelab -->`) |
| `cleanup-preview`, `cleanup-registry` | runner LXC / hébergé | à la fermeture : `preview.sh down`, label, versions GHCR `pr-<n>` |

`preview-reaper.yml` (horaire, `schedule` + `workflow_dispatch`) : `preview.sh reap` avec le TTL
(variable de dépôt `PREVIEW_TTL_HOURS`, 168 h par défaut), `down` des previews dont la PR est fermée, puis
purge des versions GHCR (`scripts/ci/purge-preview-images.sh orphans`).

- **Plafond** : `MAX_PREVIEWS` (5) en tête du workflow, vérifié par `deploy` **avant** tout pull. Une PR qui
  a déjà sa preview n'est jamais bloquée ; sinon échec explicite, résumé du job et commentaire de PR.
  Une preview dont le premier déploiement a échoué occupe sa place jusqu'à la fermeture de la PR ou au TTL.
- **Le dump** n'est téléchargé que si la preview n'a jamais été déployée avec succès. Le runner s'authentifie
  en OIDC (`azure/login`) avec l'identité `id-houseflow-dumps-reader`, par un identifiant fédéré sur le sujet
  `repo:BarbeRouss/HouseFlow:environment:preview-homelab` (`docs/azure-setup-guide.md` §5b). L'environnement
  GitHub `preview-homelab` n'a **aucun secret** : trois variables, `AZURE_TENANT_ID`,
  `AZURE_SUBSCRIPTION_ID` et `DUMPS_READER_CLIENT_ID`. La session Azure et le dump vivent dans
  `$RUNNER_TEMP` et sont supprimés à la fin du job.
- **Images** : le job `deploy` ouvre une session GHCR avec le `GITHUB_TOKEN` du job (`packages: read`), qu'il
  ferme en sortant : le déploiement marche que les paquets soient publics ou non. Après le premier push,
  passer les trois paquets `houseflow-preview-*` en **public** et donner au dépôt le rôle **Admin** sur
  chacun (sans lui, la suppression des versions échoue avec un message explicite).
- **Fermeture** : `down` s'exécute avec le script de la branche **par défaut** (pas celui de la PR).
- **Ne jamais** toucher au Traefik du homelab ni au DNS depuis un workflow : le wildcard couvre tout.

### Sécurité du runner

Le dépôt est public, le runner est persistant (pas éphémère) et membre du groupe `docker` (donc root sur la
LXC) : tout code qui y tourne peut y laisser quelque chose.

**Ce qui protège, et ce qui ne protège pas.** Sur un `pull_request` venu d'un fork, GitHub exécute la
version du workflow **de la PR**, pas celle de `main` : un fork peut retirer la condition de même dépôt, ou
ajouter son propre workflow avec `runs-on: houseflow-preview`. Les règles 1 à 3 ci-dessous et le garde-fou
(qui s'exécute lui aussi depuis la version de la PR) sont donc une défense **contre l'erreur d'un
contributeur honnête**, pas contre une PR hostile. La barrière contre un fork hostile est le **réglage GitHub
« Require approval for all outside collaborators »** (règle 4), plus le fait qu'aucun secret du dépôt ne se
trouve sur la LXC (règle 5). Ce réglage s'applique dès l'enregistrement du runner, indépendamment de cette
fusion : il est à relever en premier.

Règles 1 à 3 : appliquées par le workflow et vérifiées par `scripts/ci/check-self-hosted-guard.sh`
(job `Self-hosted Runner Guard` de `pr.yml`, avec son test sur des exemples fautifs). Règles 4 et 5 : la
barrière effective.


1. un workflow qui a un job sur le runner n'est déclenché que par `pull_request`, `workflow_dispatch`,
   `schedule` ou `push` — jamais `pull_request_target`, `issue_comment` ni `workflow_run` ;
2. dans un workflow `pull_request`, chaque job du runner porte
   `if: github.event.pull_request.head.repo.full_name == github.repository` (et exclut Dependabot) : GitHub
   l'évalue avant d'attribuer le job au runner (sur le fichier de `main`, un fork obtient des jobs `skipped` ;
   une PR de fork qui modifie le workflow n'est pas arrêtée par cette condition : voir règle 4) ;
3. droits minimaux par job (`contents: read` au niveau du workflow) ; `id-token: write` seulement pour `deploy` ;
   aucun secret du dépôt dans le workflow ;
4. **réglage GitHub** « Require approval for all outside collaborators » : aucun workflow d'un contributeur
   extérieur ne démarre sans approbation d'un porteur du droit d'écriture ;
5. aucun secret du dépôt sur la LXC, et rien de secret dans le répertoire de travail du runner en dehors de
   `PREVIEW_ROOT` (fichiers 0600).

Réglages GitHub à relever (l'App des agents n'a pas le droit de les lire : Julien/Vision, par
Settings → Actions ou `gh api`). **Non encore relevés** : à compléter avec la date et la valeur lue.

| Réglage | Valeur attendue | Relevé (date, valeur) |
|---|---|---|
| Actions → General → « Fork pull request workflows from outside collaborators » | *Require approval for all outside collaborators* | — |
| Settings → Actions → Runners | un runner au niveau du dépôt, label unique `houseflow-preview` | — |
| « Allow GitHub Actions to create and approve pull requests » | inchangé | — |
| Environnement `preview-homelab` | sans secret ; variables `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID`, `DUMPS_READER_CLIENT_ID` | — |
| Paquets `houseflow-preview-{api,web,dbtools}` | public ; dépôt en rôle Admin | — |

## Architecture arm64 et essai à la main

Les images arm64 sont construites par le workflow (`build`, #352) sur un runner hébergé `ubuntu-24.04-arm`, puis
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
