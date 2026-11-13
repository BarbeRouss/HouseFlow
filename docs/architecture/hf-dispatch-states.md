# hf-dispatch : états d'une PR et d'une issue (existant)

État **actuel** du circuit des agents, tel que le code de `hf-dispatch` le gère le 13/11/2026
(service sans IA sur `houseflow-dev`, déclenché par les webhooks GitHub). Ce document décrit
l'existant, défauts compris : la version cible sera corrigée par Julien sur cette branche, puis
servira de spécification aux tests de `hf-dispatch`.

Le processus complet, avec qui fait quoi, est dans [`hf-dispatch-flow.bpmn`](hf-dispatch-flow.bpmn)
(à ouvrir avec [bpmn.io](https://demo.bpmn.io) ou Camunda Modeler).

Légende des déclencheurs : `wh:` webhook GitHub reçu par hf-dispatch, `task:` tâche confiée à un
agent, `reconcile` rattrapage horaire.

## Cycle de vie d'une PR

```mermaid
stateDiagram-v2
    direction TB

    [*] --> NotEligible: PR de Julien non confiée
    [*] --> CiRunning: wh pull_request opened<br/>PR d'un bot, de Dependabot,<br/>ou confiée par /review /dev /product /architect
    NotEligible --> CiRunning: commande /review /dev /product /architect<br/>(table adopted)

    state "CI en cours" as CiRunning
    state "CI rouge" as CiRed
    state "Mergeabilité inconnue" as Unknown
    state "En conflit (dirty)" as Conflict
    state "En attente du train" as WaitTrain
    state "En retard sur main (behind)" as Behind
    state "En relecture" as InReview
    state "Changements demandés" as ChangesRequested
    state "Attend Julien" as WaitingJulien
    state "Approuvée (2 avis)" as Approved
    state "Fusionnée" as Merged
    state "Fermée sans fusion" as Closed
    state "Relance de l'agent qui a posé la question" as AskerFollowup
    state Evaluate <<choice>>

    CiRunning --> Evaluate: wh workflow_run completed<br/>ou reconcile
    Evaluate --> WaitingJulien: label needs:julien ou decision:julien
    Evaluate --> Unknown: mergeable_state inconnu
    Evaluate --> Conflict: mergeable_state = dirty
    Evaluate --> CiRed: un contrôle en échec
    Evaluate --> CiRunning: contrôles en cours
    Evaluate --> WaitTrain: CI verte, une autre PR tient le train
    Evaluate --> Behind: CI verte, train pris, retard sur main
    Evaluate --> InReview: CI verte, train pris, à jour

    Unknown --> Evaluate: nouvel essai après 60 s
    Conflict --> CiRunning: task dev update-branch (hors train), push
    CiRed --> CiRunning: task dev fix-ci (2 au plus par commit), push
    WaitTrain --> Evaluate: la PR du train est fusionnée, fermée ou attend Julien
    Behind --> CiRunning: task dev update-branch, push

    InReview --> Approved: wh pull_request_review approved<br/>par review ET product sur la tête
    InReview --> ChangesRequested: wh pull_request_review changes_requested
    InReview --> WaitingJulien: un agent pose needs:julien
    ChangesRequested --> CiRunning: task dev fix-review (3 au plus), push
    InReview --> Closed: PR fermée sans fusion (agent ou Julien)

    WaitingJulien --> AskerFollowup: wh issue_comment ou pull_request_review de Julien
    AskerFollowup --> WaitingJulien: réponse incomplète
    AskerFollowup --> Evaluate: l'agent retire le label<br/>(wh pull_request unlabeled)
    WaitingJulien --> Evaluate: Julien retire le label

    Approved --> Merged: auto-merge GitHub<br/>(2 approbations, branche à jour, 5 contrôles)
    Merged --> [*]: wh pull_request closed merged<br/>tâches after_merge, PR empilées reciblées,<br/>réévaluation des PR ouvertes, maybe_pick
    Closed --> [*]: maybe_pick
```

## Cycle de vie d'une issue

```mermaid
stateDiagram-v2
    direction TB

    state "Nouvelle (sans status)" as New
    state "status:spec" as Spec
    state "needs:architect" as NeedsArchitect
    state "needs:product" as NeedsProduct
    state "Attend Julien" as WaitingJulien
    state "status:ready" as Ready
    state "status:in-progress" as InProgress
    state "status:in-review" as InReview
    state "Fermée" as Closed

    [*] --> New: Julien ouvre une issue
    [*] --> Ready: un agent ouvre une issue déjà prête<br/>(sous-issues d'architect)
    New --> Spec: task product triage<br/>(wh issues opened ou reconcile)
    Spec --> NeedsArchitect: product
    NeedsArchitect --> Ready: task architect analyse
    Spec --> Ready: product
    Spec --> NeedsProduct: dev ou architect
    NeedsProduct --> Spec: task product answer
    Spec --> WaitingJulien: un agent pose needs:julien ou decision:julien
    WaitingJulien --> Spec: commentaire de Julien<br/>task followup de l'agent qui a demandé
    Ready --> InProgress: task dev pick<br/>(maybe_pick : issue prête et place libre)
    InProgress --> InReview: dev ouvre la PR
    InReview --> Closed: PR fusionnée (Closes #n)
    New --> Closed: product ferme (doublon, obsolète)
    Spec --> Closed: product ferme

    note right of Ready
        maybe_pick est appelé sur : wh issues labeled status:ready,
        fin d'un tour de dev réussi, PR fermée, label d'attente retiré,
        reconcile. Pas de pick pendant un triage de product, ni 30 min
        après un pick qui n'a rien trouvé.
    end note
```

## Commandes de Julien

- Sur une PR : `/review` (évalue la PR), `/dev` (followup de dev), `/product` (relecture
  fonctionnelle), `/architect` (analyse). La PR est alors « adoptée » par le circuit.
- Sur une issue : `/dev` (dev l'implémente), `/product` (product répond), `/architect` (analyse).
- Sur un élément qui attend Julien, tout commentaire ou avis de Julien part d'abord à l'agent qui a
  posé la question (lu dans l'historique GitHub), même s'il commence par une commande.

## Règles transverses

- Un tour à la fois par agent ; priorité des tâches : fix-main, fix-ci, puis fix-review, review,
  followup, update-branch, functional-review, puis answer, analyse, triage, pick, daily.
- Plafonds de tours par agent et par jour (`daily_caps`), délai maximal d'un tour 45 min.
- **Train** : une seule PR à la fois en mise à jour et en relecture, la plus ancienne d'abord.
- Pipeline de `main` en échec : task dev fix-main et message Discord.
- `reconcile` toutes les heures : réévalue les PR, relance les issues en needs:product,
  needs:architect et les issues de Julien non triées.

## Défauts connus de l'existant

1. GitHub n'envoie aucun événement quand une PR passe en conflit ou en retard : seuls la fusion
   d'une autre PR (réévaluation de toutes les PR) et `reconcile` le détectent.
2. Un label `needs:julien` posé sur une **PR** ne prévient pas Julien sur Discord : seul
   l'événement `issues labeled` est traité, pas `pull_request labeled`.
3. Au-delà de 2 fix-ci sur le même commit ou de 3 demandes de changements, la PR reste bloquée
   sans escalade.
4. Le train sérialise toutes les PR : chaque fusion remet les autres en retard (règle « branche
   à jour obligatoire »).
5. Les deux avis (review, product) sont évalués séparément, sans point de jonction : un avis
   arrivé après une mise à jour peut porter sur un ancien commit.
6. Une issue `status:in-review` sans PR ouverte n'est reprise par personne.
7. Les tâches ajoutées à la main (Vision) et la table `after_merge` ne sont pas visibles sur GitHub.
