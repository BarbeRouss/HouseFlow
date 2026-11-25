# Previews de PR sur le homelab, preprod sur Azure

> **Statut : décidé par Julien le 2026-11-12.** Conception et suivi : issue #348 (sous-issues #351, #352, #350, #353).
> Exploitation au quotidien : [`deploy/preview/README.md`](../../deploy/preview/README.md). Vue d'ensemble : `specs/architecture.md`.

## Contexte

Depuis le 2026-11-10, aucune preview Azure ne se crée (`SkuNotAvailable`, `RegionalAllocationFailed`,
`MaxNumberOfRegionalEnvironmentsInSubExceeded`) : plus aucune PR ne peut être validée par product. Chaque preview
Azure paie en plus un Flexible Server, un CAE, une Static Web App et des logs.

## Décision

| | PREVIEW (toute PR du dépôt, hors Dependabot) | PREPROD (`main`, avant la prod) | PROD |
|---|---|---|---|
| Où | LXC `houseflow-preview` (arm64, 6 Gio, VLAN 150) | souscription Azure jetable — **livrée (#350)** | Azure |
| Pile | projet compose `pr-<n>` : web, api, postgres, restore du dump | topologie de prod, un seul environnement | inchangée |
| Noms | `<n>.pr.houseflow.cloud`, `api-<n>.pr.houseflow.cloud` | `preprod.houseflow.cloud`, `api-preprod.houseflow.cloud` | inchangés |
| Fin | `down` à la fermeture + TTL 7 jours | destruction en fin de run + TTL 4 h | — |
| Plafond | aucun (alerte mémoire `PreviewHostMemoryHigh`) | 2 CAE (orphelin ou suppression en cours) | — |

**État au 2026-11-25.** Le code des previews Azure a été retiré (#353) : plus aucune PR ne crée de ressource Azure.
La preprod (#350) a été reconstruite depuis l'historique git (`git show ebd2f6f^:<chemin>`), mais sous une autre
forme que l'ancienne preview par PR : voir la section suivante.

## Preprod Azure : la version de `main` avant l'approbation de la prod

**Décision de BarbeRouss du 2026-11-25 (option C, #350, PR #387)** : « On a ainsi la dernière version de main,
vraiment celle qui part en prod. » La preprod n'est pas déclenchée par une PR (ni par des chemins d'infra, ni par
un label) : c'est un étage du pipeline de livraison. Dans `pipeline.yml`, les jobs `preprod-*` déploient la version
de `main` en cours de livraison (les images du job `build` de ce run) sur la topologie de production, dans la
souscription jetable, **en parallèle** de `apply-shared → certificate → plan-prod`. `approve-prod` n'est lancé que
si la preprod a réussi : plan lu et version éprouvée, puis approbation.

Pourquoi : le code se teste très bien sur le homelab (la preview de chaque PR) ; Azure ne sert qu'à éprouver ce qui
est propre à Azure, sur ce qui part réellement en production. Plus de preprod par PR, plus de label `preprod`.

Fonctionnement :

- Un seul environnement, à nom stable (`rg-houseflow-preprod`, `preprod.houseflow.cloud`, `api-preprod.houseflow.cloud` :
  un seul niveau sous la zone, le certificat est un wildcard), state propre (`environment|dns|custom-domains-preprod.tfstate`).
- Topologie de production, **sans repli** de région ni de SKU. Un refus de capacité (`SkuNotAvailable`,
  `RegionalAllocationFailed`, …) est une erreur explicite dans le résumé du run. `skip_preprod` (déclenchement manuel
  uniquement, jamais sur un push) laisse livrer quand même ; le saut est écrit dans le résumé.
- Plafond de 2 Container Apps Environments dans la région avant tout `apply` (un orphelin, un CAE en suppression).
- Propriété : deux runs peuvent viser la preprod en même temps (l'un attend l'approbation, l'autre arrive sur `main`).
  Le tag `owner-run` du resource group (`scripts/ci/preprod-owner.sh`) dit lequel la détient ; le plus récent la
  reprend, le périmé n'y touche plus. Sans lui, la destruction du run périmé détruirait la preprod du run récent.
- Destruction : `preprod-destroy` après `apply-prod` (réussi ou non), ou si l'approbation est refusée, annulée ou
  périmée ; la prod n'attend pas. Filet : `reaper.yml` (tag `ttl`, 4 h glissantes, souscription jetable seulement).
  Si l'approbation attend plus de 4 h, la preprod a disparu : le plan de production reste valide, le résumé donne
  l'échéance.
- Coût : aucun nouveau service ni SKU (Flexible Server B1ms ≈ 13 €/mois s'il tournait en continu, CAE en
  consommation, Static Web App Free) ; un seul environnement, quelques heures par livraison : < 5 €/mois en pratique.

Contrepartie assumée : une erreur d'infrastructure n'est vue qu'**après** le merge, sur `main`, avant l'approbation.
`plan-prod` montre l'effet sur la prod existante ; la preprod prouve que le code produit un environnement qui marche
depuis zéro. Une reprise d'environnement encore vivant ne prouve pas la création depuis zéro : celle-ci est éprouvée
dès que la preprod a expiré (TTL 4 h), ce qui est le cas de la plupart des runs.

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

- La preprod arrive tard (après le merge) : le fonctionnel se valide sur la preview de la PR, la preprod ne prouve que
  l'infrastructure et la livraison de la version exacte de `main`.
- Pas de TLS entre l'API et PostgreSQL en preview (réseau Docker interne du projet, données pseudonymisées,
  durée bornée).
- La LXC est un point unique : si elle tombe, plus de preview. Acceptable pour de la validation, pas pour la prod.
- Les mesures de durée (build ARM, déploiement) sont à relever sur les premiers runs ; les chiffres de #348
  (3 à 6 min contre 15 à 30 min sur la LXC) étaient des estimations.
