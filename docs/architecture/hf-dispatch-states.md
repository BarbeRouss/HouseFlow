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
    NotEligible --> cWait: Julien comments /review, /product or /architect<br/>(PR adopted, evaluated with its current CI)
    NotEligible --> CiRunning: Julien comments /dev<br/>(PR adopted, dev follow-up then push)

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
validated, or the issue sent back to product or architect. Every kind of issue (feature, bug, tech debt) goes
through every step. The labels always tell who must act next (see *Labels*), so the work can resume from GitHub
alone.

```mermaid
stateDiagram-v2
    direction TB

    state "status:spec: product writes the functional spec" as Spec
    state "status:spec + needs:architect: architect reviews" as NeedsArchitect
    state "status:spec + needs:product: product answers" as NeedsProduct
    state "status:spec + needs:julien: waiting for Julien" as WaitingJulien
    state "status:waiting-dependency: parent of sub-issues" as WaitingDependency
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
    state cReview <<choice>>

    [*] --> cCreated: issue opened<br/>Created as status:ready?
    cCreated --> Ready: [yes]
    cCreated --> cDuplicate: [no] task product<br/>Duplicate?
    cDuplicate --> Closed: [yes] product closes it
    cDuplicate --> Spec: [no] product sets status:spec
    Spec --> cInfo: Missing information,<br/>or doubt about doing it at all?
    cInfo --> WaitingJulien: [yes] product sets needs:julien and stops
    cInfo --> NeedsArchitect: [no] spec written,<br/>product sets needs:architect
    NeedsArchitect --> cArchitect: task architect<br/>Architect verdict?
    cArchitect --> NeedsProduct: [question for product]
    NeedsProduct --> NeedsArchitect: product answers
    cArchitect --> WaitingJulien: [blocked, circular discussion,<br/>any cost, not feasible or not worth it]
    cArchitect --> WaitingDependency: [split into sub-issues]<br/>sub-issues created as status:ready
    cArchitect --> Ready: [product and architect agree]<br/>architect sets status:ready
    WaitingJulien --> cAnswer: Julien comments (no label to change)<br/>Whose question was it?
    cAnswer --> Spec: [product's] product resumes
    cAnswer --> NeedsArchitect: [architect's] architect resumes
    cAnswer --> Closed: [Julien drops it] the agent closes it
    WaitingDependency --> Closed: all sub-issues closed<br/>task architect closes the parent

    Ready --> InProgress: task dev, highest priority first<br/>dev sets status:in-progress
    InProgress --> cClear: Requirements clear enough to build?
    cClear --> InReview: [yes] branch + PR,<br/>dev sets status:in-review: PR lifecycle
    cClear --> NeedsProduct: [functional gap] dev comments, no PR
    cClear --> NeedsArchitect: [technical gap] dev comments, no PR
    InReview --> cReview: PR merged, closed, or sent back?
    cReview --> Closed: [merged] (Closes #n)
    cReview --> NeedsProduct: [spec wrong, found during the PR]<br/>branch kept
    cReview --> NeedsArchitect: [design wrong, or PR closed without merge]<br/>branch kept

    note right of Ready
        When an issue comes back to status:ready after a trip back from its PR,
        dev resumes the existing branch and PR instead of starting again.
    end note
```

## Labels (target)

One `status:*` and at most one `needs:*` at a time. `decision:julien` is merged into `needs:julien` (one label
for everything Julien must answer, including any cost), which makes a simple board possible.

Allowed combinations:

- `status:spec` alone (product is writing), or with exactly one of `needs:product`, `needs:architect`,
  `needs:julien`;
- `status:ready`, `status:in-progress`, `status:in-review`, `status:waiting-dependency`: alone, never with a
  `needs:*` on the issue (questions raised during a PR go on the PR, see the PR lifecycle).

Who may set what:

- product: `status:spec`, `needs:architect`, `needs:julien`; closes duplicates. Never `status:ready`.
- architect: `needs:product`, `needs:julien`, `status:ready`, `status:waiting-dependency`; creates sub-issues;
  closes a parent whose sub-issues are all closed.
- dev: `status:in-progress`, then `status:in-review` (PR opened) or `status:spec` + `needs:product` /
  `needs:architect` (no PR, or sent back from the PR).
- review: no issue label; on the PR, `needs:julien`, or a send-back to product or architect (spec or design wrong).
- Julien: only comments; the agent who asked removes `needs:julien`.

Enforcement: agents do not edit labels with raw `gh` commands. They use one command (in the `gh` wrapper of
`hf-agents`), e.g. `hf-state <n> <status> [<needs>] --reason "..."`, which checks the role, the combination and
the transition from the current state, swaps the old labels for the new ones in one go, and posts the reason
as a comment (reused by the Discord notification). Raw label edits are refused by the wrapper. hf-dispatch also
checks every labeled webhook and reports an invalid combination on Discord; it never fixes labels itself.

## Who changes labels

Only the LLM agents and Julien change workflow labels. hf-dispatch changes none (checked in the code on
2026-11-13). Two non-LLM changes exist, outside the workflow labels: the `preview` label set and removed by
the preview workflow (GitHub Actions), and the labels Dependabot puts on its own PRs. They are not notified.

## Comment routing (target)

A comment from Julien goes to the agent that owns the item in its current state: product in `status:spec`
(alone or with `needs:product`), architect with `needs:architect`, the agent who asked with `needs:julien`, dev in
`status:in-progress`, the PR lifecycle in `status:in-review`. If that agent is busy, hf-dispatch waits for the
end of its turn, then starts it again with the new comment.

## Priority (target)

dev takes ready issues by `priority:high`, then `priority:medium`, then `priority:low`, oldest first within a
level. No more hand-edited focus in the configuration.

## Delivery guarantees (target)

At-least-once, event-driven, no polling of the agents' state:

1. hf-dispatch stores every received webhook in its SQLite database (table `webhooks`, see
   [the data model](hf-dispatch-data-model.md)) **before** answering GitHub. If it cannot, it answers an error.
   On startup, every webhook not processed yet is processed.
2. GitHub does not redeliver failed webhooks by itself, and a delivery can fail for reasons other than
   hf-dispatch being stopped (network, Traefik, timeout). The webhook is configured on the **Dispatch GitHub
   App**, so every 30 minutes, and at startup, hf-dispatch lists the app's recent deliveries
   (`GET /app/hook/deliveries`, authenticated as the app) and asks for a redelivery of every failed one
   (`POST /app/hook/deliveries/{id}/attempts`). GitHub keeps deliveries for 3 days.
3. Each delivery is processed once: the `X-GitHub-Delivery` id is the primary key of `webhooks`, so a second
   copy (a redelivery keeps the same id) is ignored.
4. A task whose agent turn fails (quota exhausted, timeout, crash) goes to `error`, Julien is told on Discord,
   and the task is retried after 30 minutes, or at the next start of hf-dispatch.
5. Agents are idempotent: a task delivered twice must not do the work twice (they check the GitHub state first).

Current state for comparison: webhooks are not stored (only one log line per webhook in the journal); they go
to an in-memory queue after the HTTP answer (lost if hf-dispatch stops), a failed task is retried once right away, then marked failed with a Discord message, and nothing retries
it later.

## Discord notifications (target, defined by Julien on 2026-11-13)

Every workflow label change made by an agent (LLM) is posted in the Discord activity channel, so Julien sees
the workflow move without opening GitHub. One message per action (label events from the same actor on the same item
within a few seconds are grouped), with the actor, what changed, a link to the item and its title, and the reason
(first line of the comment the agent wrote with the change). Examples:

- **dev** set `needs:architect` on issue [#306 MCP 3/4: write tools](https://github.com/BarbeRouss/HouseFlow/issues/306):
  "the scope of update_device is unclear: which fields can be changed?"
- **architect** set `status:ready` and removed `needs:julien` on issue #350: "Julien's answer applied, spec complete."

Labels set on a PR are notified the same way as labels set on an issue (defect 2 of the current state).
`needs:julien` also mentions Julien (except at night).

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
