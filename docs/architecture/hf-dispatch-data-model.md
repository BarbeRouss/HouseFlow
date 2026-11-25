# hf-dispatch: data model

`hf-dispatch` keeps its state in one SQLite database, `/var/lib/hf-dispatch/queue.db` (owner `hf-dispatch`),
created by the service at startup. GitHub stays the source of truth for issues, PRs, labels and reviews: the
database only holds what GitHub does not (webhooks received, agent tasks and turns, the review slot, loop
guards). `ref` is an issue or PR number of `BarbeRouss/HouseFlow`; times are Unix seconds (UTC).
State of hf-dispatch v2, 2026-11-23.

```mermaid
erDiagram
    webhooks ||--o{ tasks : "webhook_id"
    tasks ||--o{ runs : "task_id"
    webhooks {
        TEXT id PK "X-GitHub-Delivery GUID: a redelivery is ignored"
        REAL received
        TEXT event "issues, pull_request, issue_comment, workflow_run..."
        TEXT action "opened, labeled, completed..."
        INTEGER ref "issue or PR number, NULL if none"
        TEXT sender "GitHub login"
        TEXT payload "full JSON body, emptied after 30 days"
        TEXT state "inbox, done, failed"
        REAL processed
        TEXT error "why it failed"
    }
    tasks {
        INTEGER id PK
        INTEGER webhook_id FK "webhook that created the task, NULL for timers"
        TEXT agent "product, architect, dev, review"
        TEXT kind "spec, answer, analyse, close-parent, implement, resume, fix-ci, update-branch, fix-review, review, functional-review, merge, close-pr, fix-main, comment, daily"
        INTEGER ref
        INTEGER prio "lower runs first"
        TEXT state "pending, running, done, error, cancelled"
        INTEGER attempts
        REAL next_attempt "on error: now + 30 min"
        TEXT last_error "quota or rate limit, timeout, exit code"
        REAL created
        REAL updated
        TEXT note "extra context given to the agent"
    }
    runs {
        INTEGER id PK
        INTEGER task_id FK
        TEXT agent
        TEXT kind
        INTEGER ref
        REAL started
        REAL ended "NULL while running"
        INTEGER rc "0 = success"
    }
    redeliveries {
        TEXT delivery_id PK "failed delivery found through GET /app/hook/deliveries"
        REAL requested
        INTEGER count "at most 3"
    }
    slot {
        INTEGER ref PK "the single PR holding the review slot"
        REAL since
    }
    asked {
        INTEGER ref PK
        TEXT sha PK "head commit"
        TEXT kind PK
        INTEGER count "PR tasks given for this commit (loop guard)"
    }
    reported {
        TEXT k PK "Discord report already sent (stuck item, invalid labels)"
        REAL at
    }
    meta {
        TEXT k PK "daily"
        TEXT v "date of the last product daily"
    }
```

- A webhook is inserted in `webhooks` (state `inbox`) **before** hf-dispatch answers GitHub; the worker then
  processes the inbox, oldest first, and at startup everything still in `inbox`. Query:
  `SELECT datetime(received,'unixepoch','localtime'), event, action, ref, sender, state FROM webhooks ORDER BY received DESC LIMIT 50`.
- A failed turn puts its task in `error`; it goes back to `pending` when `next_attempt` has passed (checked every
  30 minutes) or at the next start.
- `status.json` (same directory) is a snapshot for the dashboard and the agents; turn logs are
  `/var/lib/hf-dispatch/logs/run-<run id>-<agent>.log`.
- Left from v1, no longer used: `deliveries`, `train`, `adopted`, `asks`, `changes`, `fixci`, `after_merge`,
  `backlog`. They can be dropped once v2 has run for a while.
