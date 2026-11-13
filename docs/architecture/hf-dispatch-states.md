# hf-dispatch: PR and issue lifecycle (current state)

**Current** behaviour of the agent workflow as implemented in `hf-dispatch` on 2026-11-13 (AI-free service on
`houseflow-dev`, driven by GitHub webhooks). This document describes what exists today, known defects included.
Julien corrects it on this branch into the target version, which then becomes the specification for the
`hf-dispatch` tests.

The full process, with who does what, is in [`hf-dispatch-flow.bpmn`](hf-dispatch-flow.bpmn) (open it with
[bpmn.io](https://demo.bpmn.io), the bpmn-io VS Code extension or Camunda Modeler).

Trigger notation: `wh` = GitHub webhook received by hf-dispatch, `task` = task handed to an agent,
`reconcile` = hourly catch-up.

## PR lifecycle

Diamonds are **decisions** taken by `evaluate_pr` (hf-dispatch): the question is on the arrow going in, the
answers are in brackets on the arrows going out. The black bars split and join the two reviews, which run in
parallel.

```mermaid
stateDiagram-v2
    direction TB

    state "Outside the workflow" as NotEligible
    state "CI running" as CiRunning
    state "CI red: dev fix-ci" as CiRed
    state "Mergeability unknown: retry in 60 s" as Unknown
    state "Conflict: dev update-branch" as Conflict
    state "Waiting for the train" as WaitTrain
    state "Behind main: dev update-branch" as Behind
    state "Code review by review" as ReviewCode
    state "Functional review by product (on the preview)" as ReviewProduct
    state "Changes requested: dev fix-review" as ChangesRequested
    state "Waiting for Julien" as WaitingJulien
    state "Follow-up by the agent who asked" as AskerFollowup
    state "Approved by review and product" as Approved
    state "Merged" as Merged
    state cEligible <<choice>>
    state cWait <<choice>>
    state cMerge <<choice>>
    state cChecks <<choice>>
    state cTrain <<choice>>
    state cBehind <<choice>>
    state cCode <<choice>>
    state cProduct <<choice>>
    state cAnswer <<choice>>
    state forkReviews <<fork>>
    state joinReviews <<join>>

    [*] --> cEligible: wh pull_request opened<br/>Eligible PR?
    cEligible --> NotEligible: [Julien's PR, not handed over]
    cEligible --> CiRunning: [bot, Dependabot or adopted PR]
    NotEligible --> CiRunning: Julien comments /review /dev /product /architect

    CiRunning --> cWait: wh workflow_run completed or reconcile<br/>Waiting label?
    cWait --> WaitingJulien: [needs:julien or decision:julien]
    cWait --> cMerge: [no]<br/>Mergeability?
    cMerge --> Unknown: [not computed yet]
    Unknown --> cWait: 60 s later
    cMerge --> Conflict: [conflict]
    Conflict --> CiRunning: dev pushes (outside the train)
    cMerge --> cChecks: [mergeable]<br/>CI checks?
    cChecks --> CiRed: [at least one failure]
    CiRed --> CiRunning: dev pushes (at most 2 fix-ci per commit)
    cChecks --> CiRunning: [still running]
    cChecks --> cTrain: [all green]<br/>Train free?
    cTrain --> WaitTrain: [held by another PR]
    WaitTrain --> cWait: the train PR is merged,<br/>closed or waits for Julien
    cTrain --> cBehind: [free, or already this PR]<br/>Behind main?
    cBehind --> Behind: [yes]
    Behind --> CiRunning: dev pushes
    cBehind --> forkReviews: [up to date] reviews requested<br/>(unless already done on this commit)

    forkReviews --> ReviewCode
    forkReviews --> ReviewProduct
    ReviewCode --> cCode: review verdict?
    ReviewProduct --> cProduct: product verdict?
    cCode --> joinReviews: [approved]
    cProduct --> joinReviews: [approved]
    cCode --> ChangesRequested: [changes requested]
    cProduct --> ChangesRequested: [changes requested]
    cCode --> WaitingJulien: [question for Julien]
    cProduct --> WaitingJulien: [question for Julien]
    joinReviews --> Approved
    ChangesRequested --> CiRunning: dev pushes (at most 3 fix-review)

    WaitingJulien --> AskerFollowup: wh comment or review by Julien
    WaitingJulien --> cWait: Julien removes the label
    AskerFollowup --> cAnswer: Does it answer the question?
    cAnswer --> WaitingJulien: [no]
    cAnswer --> cWait: [yes] the agent removes the label

    Approved --> Merged: GitHub auto-merge<br/>(2 approvals, branch up to date, 5 required checks)
    Merged --> [*]: wh pull_request closed merged:<br/>after_merge tasks, stacked PRs retargeted,<br/>open PRs re-evaluated, maybe_pick

    note right of WaitTrain
        Train: only one PR at a time goes through update, review and merge,
        oldest first. Since main requires up-to-date branches, every merge
        puts the other PRs behind, and updating a branch dismisses its
        approvals: without the train, all PRs were updated and reviewed
        again after each merge (117 agent turns for 13 PRs on 2026-11-06).
    end note
```

A PR can be closed without merge at any time (by an agent or by Julien): hf-dispatch then only calls
`maybe_pick`.

## Issue lifecycle (current)

```mermaid
stateDiagram-v2
    direction TB

    state "New (no status)" as New
    state "status:spec" as Spec
    state "needs:architect: architect analyse" as NeedsArchitect
    state "needs:product: product answer" as NeedsProduct
    state "Waiting for Julien" as WaitingJulien
    state "status:ready" as Ready
    state "status:in-progress: dev pick" as InProgress
    state "status:in-review" as InReview
    state "Closed" as Closed
    state cTriage <<choice>>
    state cPick <<choice>>

    [*] --> New: Julien opens an issue
    [*] --> Ready: an agent opens an issue that is already ready<br/>(architect sub-issues)
    New --> cTriage: task product triage<br/>(wh issues opened or reconcile)<br/>What next?
    cTriage --> Closed: [duplicate or obsolete]
    cTriage --> Spec: [needs specification]
    cTriage --> Ready: [clear enough]
    Spec --> NeedsArchitect: product
    NeedsArchitect --> Ready: architect splits and sets status:ready
    Spec --> NeedsProduct: dev or architect asks product
    NeedsProduct --> Spec: product answers
    Spec --> WaitingJulien: an agent sets needs:julien or decision:julien
    WaitingJulien --> Spec: Julien comments<br/>follow-up by the agent who asked
    Ready --> cPick: maybe_pick<br/>Can dev take new work?
    cPick --> Ready: [no: product is triaging,<br/>PR limit reached, or an empty pick < 30 min ago]
    cPick --> InProgress: [yes] task dev pick
    InProgress --> InReview: dev opens the PR
    InReview --> Closed: PR merged (Closes #n)

    note right of cPick
        maybe_pick runs on: wh issues labeled status:ready, end of a
        successful dev turn, PR closed, waiting label removed, reconcile.
    end note
```

## Issue lifecycle (target, defined by Julien on 2026-11-13)

Linear flow. Product writes the functional spec, architect reviews it and is the only one who can make an issue
ready. dev and review are not involved before `status:ready`; once ready, dev has two exits: a PR that can be
validated, or the issue sent back to product or architect. The waiting labels (`needs:product`,
`needs:architect`, `needs:julien`) always tell who must act next, so the work can resume from GitHub alone.

```mermaid
stateDiagram-v2
    direction TB

    state "status:spec: product writes the functional spec" as Spec
    state "needs:architect: architect reviews the spec" as NeedsArchitect
    state "needs:product: product answers architect" as NeedsProduct
    state "needs:julien: waiting for Julien" as WaitingJulien
    state "status:ready" as Ready
    state "status:in-progress: dev implements" as InProgress
    state "status:in-review: PR open" as InReview
    state "Closed" as Closed
    state cCreated <<choice>>
    state cDuplicate <<choice>>
    state cInfo <<choice>>
    state cArchitect <<choice>>
    state cAnswer <<choice>>
    state cClear <<choice>>

    [*] --> cCreated: issue opened<br/>Created as status:ready?
    cCreated --> Ready: [yes]
    cCreated --> cDuplicate: [no] task product<br/>Duplicate?
    cDuplicate --> Closed: [yes] product closes it
    cDuplicate --> Spec: [no] product sets status:spec
    Spec --> cInfo: Missing information?
    cInfo --> WaitingJulien: [yes] product sets needs:julien and stops
    cInfo --> NeedsArchitect: [no] spec written,<br/>product sets needs:architect
    NeedsArchitect --> cArchitect: task architect<br/>Architect verdict?
    cArchitect --> NeedsProduct: [question for product]<br/>architect sets needs:product
    NeedsProduct --> NeedsArchitect: product answers,<br/>sets needs:architect
    cArchitect --> WaitingJulien: [blocked, or discussion going round in circles]<br/>architect sets needs:julien
    cArchitect --> Ready: [product and architect agree]<br/>architect sets status:ready
    WaitingJulien --> cAnswer: Julien comments (no label to change)<br/>Whose question was it?
    cAnswer --> Spec: [product's] product resumes
    cAnswer --> NeedsArchitect: [architect's] architect resumes
    Ready --> InProgress: task dev, triggered directly<br/>dev sets status:in-progress
    InProgress --> cClear: Requirements clear enough to build?
    cClear --> InReview: [yes] dev creates the branch and the PR,<br/>sets status:in-review: PR lifecycle
    cClear --> NeedsProduct: [functional gap] dev comments, no PR,<br/>sets status:spec + needs:product
    cClear --> NeedsArchitect: [technical gap] dev comments, no PR,<br/>sets status:spec + needs:architect
    InReview --> Closed: PR merged (Closes #n)

    note right of cArchitect
        Labels each agent may set:
        product: status:spec, needs:architect, needs:julien (never status:ready)
        architect: needs:product, needs:julien, status:ready
        dev: status:in-progress, then either status:in-review (PR opened)
             or status:spec + needs:product / needs:architect (no PR)
        Julien only comments: the agent who asked removes needs:julien.
        No hard cap on the product/architect ping-pong: their instructions say
        to set needs:julien when the discussion goes round in circles.
    end note
```

## Julien's commands

- On a PR: `/review` (evaluate the PR), `/dev` (dev follow-up), `/product` (functional review),
  `/architect` (analysis). The PR is then adopted by the workflow.
- On an issue: `/dev` (dev implements it), `/product` (product answers), `/architect` (analysis).
- On an item waiting for Julien, any comment or review from Julien first goes to the agent who asked (read
  from the GitHub label history), even when it starts with a command.

## Cross-cutting rules

- One turn at a time per agent. Task priority: fix-main, fix-ci, then fix-review, review, follow-up,
  update-branch, functional-review, then answer, analyse, triage, pick, daily.
- Daily turn caps per agent (`daily_caps`), 45 min maximum per turn.
- **Train**: one PR at a time in update and review, oldest first (see the note on the PR diagram).
- Pipeline of `main` red: task dev fix-main and a Discord message.
- `reconcile` every hour: re-evaluates PRs, re-queues issues in needs:product or needs:architect and Julien's
  untriaged issues.

## Known defects of the current state

1. GitHub sends no event when a PR becomes conflicting or behind: only the merge of another PR (all open
   PRs re-evaluated) and `reconcile` detect it.
2. A `needs:julien` label set on a **PR** does not notify Julien on Discord: only `issues labeled` is
   handled, not `pull_request labeled`.
3. Beyond 2 fix-ci on the same commit or 3 change requests, the PR stays stuck with no escalation.
4. The train serialises all PRs: every merge puts the others behind ("require up-to-date branch" rule).
5. The two verdicts are evaluated independently: a verdict given before an update may refer to an older
   commit.
6. An issue in `status:in-review` without an open PR is picked up by nobody.
7. Tasks added by hand (Vision) and the `after_merge` table are not visible on GitHub.
