# `bench/` — harnais de comparaison de performance Rust vs .NET

Ce répertoire mesure, dans les mêmes conditions, le backend .NET (`src/HouseFlow.API`)
et son portage Rust (`rust/houseflow-api`), puis produit le rapport
`docs/perf/rust-vs-dotnet.md`.

Tout est reproductible **depuis le devcontainer** : bash, `curl`, `jq`, `psql`,
`python3` (stdlib), `dotnet`, `cargo`, et un générateur de charge.

## Démarrage rapide

```bash
# Campagne complète : .NET, puis Rust, puis le rapport
bash bench/run-comparison.sh

# Un seul backend (les colonnes de l'autre restent en « n/a »)
bash bench/run-comparison.sh --only dotnet

# Réutiliser les artefacts déjà construits (itération rapide)
bash bench/run-comparison.sh --skip-build

# Campagne courte pour vérifier que le harnais marche
BENCH_DURATION=3s bash bench/run-comparison.sh --only dotnet
```

Hors devcontainer, sur cette machine, `dotnet` n'est pas dans le `PATH` par défaut :
les scripts ajoutent `/usr/share/dotnet` d'eux-mêmes, mais un `export PATH="/usr/share/dotnet:$PATH"
DOTNET_ROOT=/usr/share/dotnet` ne fait pas de mal.

## Le générateur de charge : `oha`

[`oha`](https://github.com/hatoo/oha) (Rust) est le générateur par défaut :

```bash
cargo install oha          # binaire dans ~/.cargo/bin/oha
```

**À ajouter au `Dockerfile` du devcontainer** (le fichier appartient à un autre
périmètre, il n'est pas modifié ici) :

```dockerfile
RUN cargo install oha --locked --root /usr/local
```

Si `oha` est absent et que `npx` est disponible, `scenarios.sh` bascule
automatiquement sur [`autocannon`](https://github.com/mcollina/autocannon)
(`npx autocannon@7`) et le signale sur stderr. Dans ce mode, `autocannon` ne publie
pas de p95 : la colonne p95 du rapport contient alors le p97,5 (noté dans le JSON
normalisé, champ `notes`). Forcer l'un ou l'autre : `BENCH_LOAD_TOOL=oha|autocannon`.

## Les scripts

| Script | Rôle |
|---|---|
| `run-comparison.sh` | Orchestre tout : `.NET`, puis Rust, puis le rapport. Options `--only dotnet\|rust`, `--skip-build`, `--out-dir`, `--report`. |
| `run-backend.sh <dotnet\|rust> <out_dir>` | Build release, base dédiée vierge, démarrage, mesure du démarrage, seed, scénarios, métriques process, arrêt propre. |
| `seed.sh <base_url> <out_json>` | Crée le jeu de données via l'API publique et écrit le JWT, les identifiants rejouables et les ids utiles. |
| `scenarios.sh <base_url> <seed_json> <out_dir>` | Rejoue la liste figée des scénarios avec `oha`, un JSON normalisé par scénario. |
| `report.py` | Agrège les deux répertoires de résultats en `docs/perf/rust-vs-dotnet.md` (stdlib seule). |

Chaque script s'utilise aussi seul, contre un serveur déjà démarré :

```bash
bash bench/seed.sh http://localhost:5203 /tmp/seed.json
BENCH_DURATION=5s bash bench/scenarios.sh http://localhost:5203 /tmp/seed.json /tmp/res
python3 bench/report.py --dotnet /tmp/res --out /tmp/rapport.md --no-copy-raw
```

## Les scénarios

Ils sont figés : leurs noms apparaissent tels quels dans le rapport.

| Scénario | Requête | Ce que ça mesure |
|---|---|---|
| `health` | `GET /health` | Plancher du framework (routage + sonde, pas de métier) |
| `login` | `POST /api/v1/auth/login` | bcrypt cost 11 + écriture du refresh token — **CPU bound**, tourne à concurrence réduite et sur plusieurs comptes |
| `houses_list` | `GET /api/v1/houses` | Lecture liste, jointure d'appartenance |
| `house_detail` | `GET /api/v1/houses/{id}` | Lecture + calcul des scores |
| `devices_list` | `GET /api/v1/houses/{id}/devices` | Lecture liste plus volumineuse |
| `device_detail` | `GET /api/v1/devices/{id}` | Lecture + types d'entretien + statuts |
| `upcoming_tasks` | `GET /api/v1/upcoming-tasks` | Agrégation transverse à toutes les maisons |
| `maintenance_history` | `GET /api/v1/devices/{id}/maintenance-history` | Lecture d'historique |
| `create_instance` | `POST /api/v1/maintenance-types/{id}/instances` | Écriture + journal d'audit |
| `create_device` | `POST /api/v1/houses/{id}/devices` | Écriture simple |

Les lectures passent **avant** les écritures : les scénarios d'écriture font grossir
le jeu de données pendant leur propre run, on évite ainsi qu'ils faussent les lectures.
Un jeton JWT frais est pris avant chaque scénario (durée de vie : 15 minutes), et une
requête à blanc valide le payload avant la mesure — un 4xx à ce moment-là arrête la
campagne au lieu de produire un joli graphique d'erreurs.

`login` tourne sur **plusieurs comptes** (`BENCH_SEED_LOGIN_USERS`, 16 par défaut,
alternés par `oha -Z`). Marteler un seul compte ne mesure pas bcrypt mais la contention
sur ses propres sessions : `AuthService.StartSessionAsync` purge les sessions au-delà de
`MaxSessionsPerUser` (10), et deux connexions simultanées du même compte se disputent la
suppression des mêmes lignes — côté .NET, EF lève alors `DbUpdateConcurrencyException` et
l'API répond 500 (constaté : 18 requêtes sur 66 à concurrence 8, un seul compte).
Avec `autocannon`, qui ne sait pas alterner les corps de requête, le scénario retombe sur
un compte unique et ces 500 réapparaissent : le rapport les compte comme des erreurs.

## Réglages

| Variable | Défaut | Effet |
|---|---|---|
| `BENCH_DURATION` | `15s` | Durée de la mesure par scénario |
| `BENCH_CONCURRENCY` | `64` | Connexions simultanées |
| `BENCH_LOGIN_CONCURRENCY` | `8` | Idem pour `login` (bcrypt sature le CPU bien avant le réseau) |
| `BENCH_WARMUP` | `1s` | Run de chauffe jeté avant chaque mesure (`0` pour le désactiver) |
| `BENCH_LOAD_TOOL` | `auto` | `oha` ou `autocannon` |
| `BENCH_ONLY` | — | Liste de scénarios séparés par des virgules (`BENCH_ONLY=health,login`) |
| `BENCH_IGNORE_SMOKE` | `0` | `1` : continuer même si la requête à blanc n'est pas 2xx |
| `BENCH_SEED_HOUSES` / `_DEVICES` / `_TYPES` / `_INSTANCES` | `3` / `10` / `3` / `2` | Volume du jeu de données |
| `BENCH_SEED_LOGIN_USERS` | `16` | Comptes alternés par le scénario `login` |
| `BENCH_SKIP_BUILD` | `0` | `1` : réutiliser les artefacts existants |
| `BENCH_COLD_BUILD` | `0` | `1` : nettoyer avant de construire, pour mesurer un build à froid |
| `BENCH_PORT` / `BENCH_DB` | `5223`/`5224`, `houseflow_bench_<backend>` | Port et base dédiés |
| `BENCH_READY_TIMEOUT` | `180` | Secondes d'attente de la sonde `/swagger/index.html` |
| `BENCH_JWT_KEY` | clé de dev | Clé HS256, **la même pour les deux backends** |
| `POSTGRES_HOST` | `localhost` | `postgres` dans le devcontainer |

Augmenter la durée donne des chiffres plus stables ; 15 s par scénario suffit à sortir
du bruit sur une machine calme. Pour explorer la saturation, faire varier
`BENCH_CONCURRENCY` (16 / 64 / 256) plutôt que la durée.

## Ce qui est mesuré en plus des requêtes

`run-backend.sh` écrit `<out_dir>/process.json` :

- **démarrage** : du lancement du processus au premier 200 sur `/swagger/index.html` ;
- **mémoire** : `VmRSS` relevé dans `/proc/<pid>/status` avant le seed, après le seed et
  après le dernier scénario, plus le pic `VmHWM` ;
- **CPU** : `utime + stime` de `/proc/<pid>/stat`, delta sur la campagne ;
- **taille** : artefact principal (`HouseFlow.API.dll` / `houseflow-api`) et livrable complet
  (le répertoire de publication pour .NET, le binaire pour Rust) ;
- **build** : durée du build, et s'il était à froid.

## Isolation

Chaque backend tourne seul, sur son propre port (5223 .NET, 5224 Rust) et sa propre base
(`houseflow_bench_dotnet`, `houseflow_bench_rust`), **droppée puis recréée** au début de
chaque run, et migrée par le backend lui-même (`ASPNETCORE_ENVIRONMENT=Development` /
`APP_ENV=Development`, `DEMO_MODE=false`). Aucune base de dev ou de test n'est touchée.
`run-backend.sh` arrête son serveur via un `trap` (SIGTERM puis SIGKILL), et
`run-comparison.sh` vérifie après chaque run que plus rien n'écoute sur le port.

## Sorties

```
bench/results/<backend>/       # non commité (.gitignore)
├── seed.json                  # JWT, identifiants, ids du jeu de données
├── scenario-<nom>.json        # résultat normalisé + sortie brute du générateur
├── raw/<nom>.json             # sortie brute du générateur, telle quelle
├── process.json               # démarrage, RSS, CPU, tailles, build
├── server.log                 # sortie du backend
└── build.log

docs/perf/rust-vs-dotnet.md    # rapport commité (français)
docs/perf/raw/<backend>/       # copie des JSON ci-dessus (seed expurgé de ses secrets)
```

## Précaution de lecture

**Les chiffres dépendent de la machine.** Le générateur de charge, le serveur et
PostgreSQL tournent ici sur le même hôte : les valeurs absolues n'ont de sens que
relativement l'une à l'autre, dans une même campagne. Un rapport produit sur un poste
ne se compare pas à un rapport produit sur un autre, ni à un chiffre de production.
Ce qui se transporte, c'est l'ordre de grandeur des **rapports** entre les deux backends.
