# Previews de PR sur le homelab, preprod sur Azure

> **Statut : décidé par Julien le 2026-11-12.** Conception et suivi : issue #348 (sous-issues #351, #352, #350, #353).
> Exploitation au quotidien : [`deploy/preview/README.md`](../../deploy/preview/README.md). Vue d'ensemble : `specs/architecture.md`.

## Contexte

Depuis le 2026-11-10, aucune preview Azure ne se crée (`SkuNotAvailable`, `RegionalAllocationFailed`,
`MaxNumberOfRegionalEnvironmentsInSubExceeded`) : plus aucune PR ne peut être validée par product. Chaque preview
Azure paie en plus un Flexible Server, un CAE, une Static Web App et des logs.

## Décision

| | PREVIEW (toute PR du dépôt, hors Dependabot) | PREPROD (PR d'infrastructure) | PROD |
|---|---|---|---|
| Où | LXC `houseflow-preview` (arm64, 6 Gio, VLAN 150) | souscription Azure jetable — **à venir (#350)** | Azure |
| Pile | projet compose `pr-<n>` : web, api, postgres, restore du dump | topologie de prod | inchangée |
| Noms | `<n>.pr.houseflow.cloud`, `api-<n>.pr.houseflow.cloud` | `pr-<n>.houseflow.cloud` | inchangés |
| Fin | `down` à la fermeture + TTL 7 jours | destruction + TTL 4 h | — |
| Plafond | aucun (alerte mémoire `PreviewHostMemoryHigh`) | 2 CAE | — |

**État au 2026-11-13.** Le code des previews Azure est retiré (#353) : `pr-preview.yml`, `reaper.yml`,
`destroy-environment.sh` et `instances/pr.tfvars` n'existent plus, et plus aucune PR ne crée de ressource Azure
(les restes ont été détruits avant la suppression). La colonne PREPROD ci-dessus est la cible de #350,
**reportée** (décision de Julien du 2026-11-12 : le MCP d'abord) : elle sera reconstruite depuis l'historique git.
D'ici là : previews sur le homelab, production sur Azure, pas d'environnement Azure intermédiaire.

## Chaîne réseau

client → Traefik du homelab (TLS, certificat wildcard `*.pr.houseflow.cloud`, `geoblock-be`) → HTTP →
Traefik de la LXC (provider Docker, labels `Host` posés par chaque projet compose, aucun port publié, aucun
fichier édité) → conteneurs. L'API fait confiance aux deux proxys (`X-Forwarded-For`/`-Proto`,
`HttpEdge:ForwardLimit=2`) pour connaître l'IP du client et se savoir en HTTPS. Le wildcard couvre toutes les
previews : aucun workflow ne touche au Traefik du homelab ni au DNS.

## Build et déploiement (#352)

- **Les images sont construites par GitHub, pas par la LXC** (option B de #348) : `linux/arm64` natif sur
  `ubuntu-24.04-arm`, poussées sur GHCR (`houseflow-preview-{api,web,dbtools}:pr-<n>`, paquets distincts de ceux
  de la prod), tirées par la LXC. Pourquoi : la LXC est la ressource rare (Cortex-A72, 6 Gio pour toutes les previews) ;
  un build Blazor WASM par push la saturerait. Coût : un job de plus et la purge des tags `pr-<n>`.
  Repli si les runners ARM hébergés disparaissent : `docker build` local sur la LXC sous les mêmes noms, puis
  `preview.sh up --no-pull` (documenté dans le README).
- **Le runner ne teste rien** : build .NET, tests unitaires, d'intégration et E2E restent sur `ubuntu-latest`
  (`pr.yml`). Pas d'E2E contre la preview.
- **Pas de plafond** (décision de Julien, 2026-11-13) : toute PR a sa preview sur ses événements, aucune attente de
  place. Garde-fou : l'alerte `PreviewHostMemoryHigh` (mémoire de la LXC > 85 %).
  Le runner ne fait tourner qu'un job à la fois : deux PR ne peuvent pas se doubler.
- **Le dump** (`db-dumps/latest.dump`, pseudonymisé) est lu en OIDC avec `id-houseflow-dumps-reader`
  (lecture seule), uniquement tant que la base de la preview n'est pas restaurée, puis supprimé du disque.
- **Nettoyage** : à la fermeture (`down`, label, versions GHCR), et `preview-reaper.yml` en filet (TTL 7 jours
  glissants depuis le dernier déploiement, PR fermées, versions sans tag).

## Sécurité du runner

Le dépôt est **public** et le compte `BarbeRouss` est un compte utilisateur (pas de *runner groups*). Un
runner enregistré **au niveau du dépôt** n'est utilisable que par ce dépôt ; il est **persistant**, ce qui
est acceptable parce que seuls les porteurs du droit d'écriture (agents, Julien) y font tourner du code. Pour
qu'un fork n'y arrive pas :

> Limite : sur un `pull_request` de fork, GitHub exécute la version du workflow **de la PR**. Les points 1 à 3
> et 6 (condition de même dépôt, garde-fou) protègent contre l'erreur d'un contributeur honnête, pas contre une
> PR hostile qui les retirerait. La barrière effective est le point 4 (approbation de tous les contributeurs
> extérieurs) et l'absence de secret sur la LXC (point 5).

1. `preview.yml` : `pull_request` et `workflow_dispatch` seulement ; jamais `pull_request_target`,
   `issue_comment` ni `workflow_run` ;
2. condition `head.repo.full_name == github.repository` (et auteur ≠ Dependabot) sur chaque job du runner et sur
   le build, évaluée **par GitHub avant** l'attribution au runner ;
3. label de runner dédié `houseflow-preview` (le runner n'a pas `self-hosted`, `linux` ni `ARM64`) ;
4. approbation requise pour tous les contributeurs extérieurs (réglage GitHub) ;
5. droits minimaux par job, aucun secret du dépôt, OIDC pour le seul dump ;
6. `scripts/ci/check-self-hosted-guard.sh` en CI : refuse un job auto-hébergé sans la condition, ou dans un
   workflow déclenché par un événement hors `pull_request`/`workflow_dispatch`/`schedule`/`push`.

La suppression à la fermeture et le reaper tournent avec le script de la branche par défaut, pas celui d'une PR.

## Compromis assumés

- Une PR d'infrastructure aura deux environnements (preview + preprod) : le fonctionnel se valide sur la
  preview, la preprod ne prouve que l'infrastructure.
- Pas de TLS entre l'API et PostgreSQL en preview (réseau Docker interne du projet, données pseudonymisées,
  durée bornée).
- La LXC est un point unique : si elle tombe, plus de preview. Acceptable pour de la validation, pas pour la prod.
- Les mesures de durée (build ARM, déploiement) sont à relever sur les premiers runs ; les chiffres de #348
  (3 à 6 min contre 15 à 30 min sur la LXC) étaient des estimations.
