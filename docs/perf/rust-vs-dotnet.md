# Performances : backend Rust vs backend .NET

Rapport généré par `bench/report.py` à partir des mesures de `bench/run-comparison.sh`.
Ne pas éditer à la main : toute modification est écrasée à la campagne suivante.

## Environnement

| Élément | Valeur |
|---|---|
| CPU | Intel(R) Xeon(R) Processor @ 2.80GHz (4 cœurs logiques) |
| RAM | 15.7 Go |
| OS | Ubuntu 24.04.4 LTS |
| Noyau | 6.18.44-fc-v33 |
| .NET SDK | 10.0.401 |
| rustc | 1.94.1 (e408947bf 2026-03-25) |
| PostgreSQL | 16.13 |
| Date | 2026-09-18 22:43 UTC |
| Commit | 3e1acfe |

## Méthodologie

- **Builds** : les deux backends sont compilés en configuration *release*
  (`dotnet publish -c Release`, `cargo build --release`), jamais en debug.
- **Base de données** : même serveur PostgreSQL, même schéma, une base dédiée et vierge
  par backend — `houseflow_bench_dotnet` (.NET) / `houseflow_bench_rust` (Rust) —, migrée au démarrage par le backend lui-même.
- **Jeu de données** : identique des deux côtés, créé via l'API publique (3 maisons, 30 appareils, 90 types d'entretien, 180 entretiens passés).
  Les entretiens sont datés dans le passé pour que scores, statuts et prochaines
  échéances soient non triviaux.
- **Charge** : `oha`, 64 connexions simultanées, 15s de mesure par
  scénario, précédées d'un run de chauffe jeté. Le scénario `login` tourne à
  8 connexions : bcrypt (cost 11) sature le CPU bien avant le réseau.
- **Jeton** : un JWT frais est obtenu avant chaque scénario (durée de vie 15 min).
- **Ordre** : les scénarios de lecture passent avant les scénarios d'écriture, pour que
  les lectures mesurent toutes le même volume de données.
- **Isolation** : un seul backend tourne à la fois, sur sa propre base et son propre port.

## Scénarios

| Scénario | Endpoint |
|---|---|
| `health` | Sonde `GET /health` (plancher framework) |
| `login` | `POST /api/v1/auth/login` (bcrypt cost 11 + écritures) |
| `houses_list` | `GET /api/v1/houses` |
| `house_detail` | `GET /api/v1/houses/{id}` (avec scores) |
| `devices_list` | `GET /api/v1/houses/{id}/devices` |
| `device_detail` | `GET /api/v1/devices/{id}` |
| `upcoming_tasks` | `GET /api/v1/upcoming-tasks` |
| `maintenance_history` | `GET /api/v1/devices/{id}/maintenance-history` |
| `create_instance` | `POST /api/v1/maintenance-types/{id}/instances` (écriture + audit) |
| `create_device` | `POST /api/v1/houses/{id}/devices` (écriture) |

### Débit et erreurs

| Scénario | .NET req/s | Rust req/s | Rapport | .NET erreurs | Rust erreurs |
|---|---|---|---|---|---|
| `health` | 8 137 | 16 546 | ×2.03 | 0 | 0 |
| `login` | 24 | 29 | ×1.22 | 4 | 0 |
| `houses_list` | 478 | 1 591 | ×3.33 | 0 | 0 |
| `house_detail` | 1 018 | 2 795 | ×2.75 | 0 | 0 |
| `devices_list` | 1 298 | 3 259 | ×2.51 | 0 | 0 |
| `device_detail` | 1 398 | 3 180 | ×2.27 | 0 | 0 |
| `upcoming_tasks` | 668 | 2 019 | ×3.02 | 0 | 0 |
| `maintenance_history` | 1 394 | 3 062 | ×2.20 | 0 | 0 |
| `create_instance` | 1 407 | 2 634 | ×1.87 | 0 | 0 |
| `create_device` | 1 929 | 3 681 | ×1.91 | 0 | 0 |

Rapport = req/s Rust ÷ req/s .NET (supérieur à ×1 : Rust plus rapide).

### Latences (ms)

| Scénario | .NET p50 | Rust p50 | .NET p95 | Rust p95 | .NET p99 | Rust p99 |
|---|---|---|---|---|---|---|
| `health` | 7.20 | 3.47 | 13.60 | 7.32 | 17.99 | 9.12 |
| `login` | 331.30 | 271.87 | 498.30 | 412.35 | 584.46 | 575.47 |
| `houses_list` | 126.75 | 39.60 | 222.15 | 50.40 | 281.24 | 56.10 |
| `house_detail` | 60.06 | 22.49 | 99.33 | 30.23 | 110.93 | 34.12 |
| `devices_list` | 48.28 | 19.08 | 74.76 | 26.82 | 85.58 | 30.80 |
| `device_detail` | 45.09 | 19.77 | 67.14 | 26.40 | 76.11 | 29.95 |
| `upcoming_tasks` | 89.14 | 31.06 | 168.04 | 41.34 | 198.78 | 46.42 |
| `maintenance_history` | 45.30 | 20.66 | 67.20 | 26.73 | 75.63 | 29.92 |
| `create_instance` | 44.34 | 23.94 | 62.31 | 29.21 | 74.56 | 34.59 |
| `create_device` | 31.87 | 16.98 | 48.49 | 21.27 | 62.87 | 24.84 |

## Métriques process

| Métrique | .NET | Rust |
|---|---|---|
| Démarrage → premier 200 (ms) | 3 783 | 263 |
| RSS avant le seed (Mo) | 166.9 | 8.9 |
| RSS après le seed (Mo) | 225.8 | 9.9 |
| RSS après les scénarios (Mo) | 315.9 | 17.0 |
| Pic RSS observé (Mo) | 341.1 | 17.0 |
| CPU consommé sur la campagne (s) | 361.7 | 343.8 |
| Artefact principal (Mo) | 0.1 | 10.2 |
| Livrable complet (Mo) | 17.5 | 10.2 |
| Temps de build (s) | 4.0 | 0.2 |
| Build à froid | non | non |
| Runtime | dotnet 10.0.401 | rustc 1.94.1 (e408947bf 2026-03-25) |

Le RSS est relevé dans `/proc/<pid>/status` (`VmRSS`, pic `VmHWM`), le CPU dans
`/proc/<pid>/stat` (`utime + stime`) entre le début du seed et la fin du dernier scénario.

## Résumé

Sur les 10 scénarios communs, Rust soutient en moyenne géométrique **×2.23** le débit de .NET. L'écart le plus favorable à Rust est `houses_list` (×3.33), le moins favorable `login` (×1.22). Côté fiabilité, 4 erreur(s) HTTP côté .NET et 0 côté Rust. Le démarrage passe de 3 783 ms (.NET) à 263 ms (Rust), le RSS en fin de campagne de 315.9 Mo à 17.0 Mo.

## Limites de lecture

- Les chiffres dépendent de la machine (cœurs, charge concurrente, disque, conteneur).
  Seuls les **rapports** entre les deux backends mesurés dans la même campagne ont du sens ;
  les valeurs absolues ne se comparent pas d'une machine à l'autre.
- Le générateur de charge tourne sur la même machine que le serveur et que PostgreSQL :
  il consomme du CPU, ce qui plafonne les scénarios les plus légers (`health` en premier).
- Les scénarios d'écriture font grossir le jeu de données pendant leur propre run ;
  ils passent en dernier pour ne pas fausser les lectures.
- Le backend .NET embarque Hangfire (jobs récurrents) là où le port Rust utilise une tâche
  `tokio` : une partie du démarrage et de la mémoire de .NET vient de là.
- `login` mesure d'abord bcrypt (cost 11), identique des deux côtés : c'est un plancher
  CPU commun, pas une différence de framework. Le scénario alterne sur plusieurs comptes :
  marteler un seul compte mesurerait la contention sur ses propres sessions (purge
  au-delà de `MaxSessionsPerUser`) plutôt que le coût d'une authentification.

## Données brutes

Les JSON produits par `oha` et les métriques process sont conservés sous `raw/`
(un sous-répertoire par backend : résultats normalisés, sorties brutes du générateur,
`process.json`, `seed.json` expurgé de ses secrets et `server.log`).
