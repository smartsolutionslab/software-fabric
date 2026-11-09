# ADR-0004: GitHub is the coordination bus

**Status:** Proposed
**Date:** 2026-10-30

## Context

Generated projects — and this repo from M2 on — are worked on by 10–20
agents at once, on different runtimes: Claude Code cloud sessions, local
Claude Code, GitHub Actions, and other models such as Codex. Developers
work on the same board. Project management happens in GitHub.

## Decision

**Agents talk to GitHub, never to each other.** Any runtime that follows
this protocol can take part.

**Board.** One GitHub Project with fields `Status` (Todo, Claimed,
In Progress, In Review, Blocked, Done), `Lane`, `Executor`, `Priority`,
`Size`.

**Eligibility.** An agent takes an issue only if it carries
`agent:ready`, has **no human assignee**, carries none of `agent:blocked`,
`agent:claimed` or `needs:*`, and its lane is one the agent serves. An
escalation no owner had capacity for has no assignee, so its `needs:*`
label is what holds it for a developer. `agent:claimed` is
set on a claim and removed when it ends, so a claimed issue looks taken
even without a board; the branch, not the label, is the lock. The lane is a `lane:<name>` label, so
eligibility needs no board access. A human assignee means the issue
belongs to that human, without exception.

**Claim.** The branch is the lock. The agent pushes an empty claim
commit to `agent/<issue>`; a second agent's claim, built on a
different commit, is not a fast-forward and is rejected, so it moves to the
next issue. The name holds nothing that can change: with the title in it, two
agents that each saw no branch could push two locks for one issue. The winner then sets `Status` and `Executor` and posts a claim
comment. An agent sent to one issue names it (`next.sh <lane> <executor>
<issue>`): it claims that issue if it is eligible, and otherwise refuses with
the reason rather than taking another, so steering an agent never means
taking `agent:ready` off the issues before it.

**Lease.** Every push renews the claim. A scheduled janitor workflow
returns an issue to Todo when its branch's last commit is older than the
lease period (default 2 h) and it has no open PR. Released, escalated and
lapsed claims **park** the branch: a release commit goes on top, the work
stays, and the next agent resumes it with a new claim commit. Branches are
never renamed, deleted or force-pushed.

**Escalation to a developer.** Three routes, each with a label and a
concrete question or the verbatim failure in a comment:

| Trigger | Label |
|---|---|
| Triage finds the issue unfit for agents | `needs:human` |
| An agent needs a decision mid-work | `needs:decision` |
| A phase fails twice | `agent:blocked` |

The assignee is chosen from the lane's owners (`docs/owners.yml` in the
generated project), least open assignments first, respecting a WIP limit
per developer. When the developer answers and unassigns themselves, a
workflow returns the issue to Todo; the next agent resumes on the
existing branch.

**Review across model families.** The reviewing model is from a different
family than the authoring one. Reviews arrive as check runs with
structured findings and are required checks. The author weighs findings
— they are input, not instructions — and after two review rounds an
unresolved PR goes to `agent:blocked`. Merge through the merge queue on
green CI and passed reviews.

**The gate is not the author's to change.** A pull request must not pick its
own reviewer or post its own verdict, so the review workflow runs the board
scripts, the findings schema and the reviewer briefs from the base branch.
The workflow file itself still comes from the pull request (GitHub runs a
`pull_request` workflow as the head defines it; `pull_request_target` would
hand the reviewer's API keys to a job that reads untrusted code). So
`.github/` has a code owner, and the default branch's protection requires the
code owner's review and the review check: a change to the gate needs a human.

**Traceability.** Every agent commit carries an `Agent-Executor:` trailer
`<family>-<runtime>/<detail>` (e.g. `claude-cloud/lane-billing`,
`claude-local/<host>`, `codex-cloud/lane-web`). The family, the part
before the first `-`, decides who reviews.

## Consequences

- No central dispatcher to keep alive; state lives on the board.
- Agents that crash cost a lease period, not a stuck issue.
- Throughput is bounded by CI capacity, the merge queue, and how fast
  developers answer escalations — not by the number of agents.
- The protocol is text plus GitHub primitives, so it is testable: the
  janitor and return-to-Todo workflows get their own tests.
- *As built (M1-08):* the generated project carries the protocol as
  scripts in `.github/scripts/board/` (pack `base.fabric`), each step one
  script, called by the workflows and by agents of any runtime. The pilot
  (M1-10) found that Claude Code's cloud sessions refuse GraphQL and
  branch writes through the API, so the scripts use REST for issues and
  `git push` for branches; a branch rename or delete is never needed. The merge
  queue and the two-review-round limit are rules in AGENTS.md, not yet
  automation.
- *As built (#96):* the reviewer runs as GitHub Copilot CLI, headless,
  instead of each vendor's action and API key. The family rule is
  unchanged; the family picks the model, pinned in `cross-model-review.yml`
  with the CLI's version. The workflow's own token was refused ("Access
  denied by policy settings"), since Copilot is licensed to the owner, not
  the organisation, so Copilot signs in with the owner's fine-grained token
  (Copilot Requests) from the `COPILOT_GITHUB_TOKEN` secret, and each review
  spends the owner's premium requests. A missing secret fails the review.
- *As built (#98):* once the organisation had Copilot, its policy "Copilot
  CLI billed to the organization" and both models, the workflow's own token
  (`copilot-requests: write`) signed in, so the review uses no secret and
  is billed to the organisation. The verdict moved out of the reviewer's
  job: `review` holds no token that can write to the pull request and hands
  its answer on as an artifact; `report` checks the gate out of the base
  branch afresh and posts it. The CLI cannot enforce the schema, so the
  findings are the last top-level object of that shape in the answer, and
  an answer without one still fails.

## Amendment (2026-11-09, #474)

The owner's decisions on #474, after Copilot's usage limit ("trust tier usage limit reached") crashed the review of
every open pull request on 2026-11-09 and nothing ran them again. The text above stands; where this section differs,
it takes precedence.

**A usage limit postpones the review; it does not fail it.** A limit is not a finding. The review recognises
Copilot's usage and rate limit errors and records the review as postponed: no job fails, and nothing is posted on the
pull request, least of all "no findings". The gate stays closed, since a postponed review is never a pass and nothing
merges unreviewed. So the verdict is no longer the review's jobs but the commit status `cross-model-review/verdict` on
the pull request's head, which the report job sets: `success` when nothing blocks, `failure` when a finding blocks or
the answer has no findings, and `pending` while the review is postponed. That status is "the review check" the
default branch's protection requires.

**No fallback model.** #474 first asked for one retry with another model of the reviewing family before
postponing. The owner withdrew it in answer to the escalation on #478: the limit ("trust tier usage limit") is the
account's, not a model's, so a limited review is postponed at once and the hourly retry takes it from there.

**Postponed reviews are retried on a schedule.** The workflow `cross-model-review-retry` runs hourly and on demand.
It finds the open pull requests whose verdict at their current head is pending as postponed and re-runs the review
run that status links to. It uses REST only and holds `actions: write` with read access to pull requests and
statuses, the least that works, and reviews nothing itself.

**An unchanged diff keeps its verdict.** When the pull request's own diff (`git diff origin/<base>...<head>`) is
identical to that of the last head that received a real verdict, after a push that only merged the base branch for
example, that verdict, pass or fail, is carried over and Copilot is not called. Each review the workflow posts
records the diff's fingerprint (SHA-256) with its verdict in a hidden line of its body. Any change to the pull
request's own diff is reviewed afresh.

The security properties above are kept: the reviewer's job still holds no token that can write to the pull request,
and the gate's files come from the base branch. A gate script the base branch does not have yet is not run from the
pull request: no verdict is kept and no status is set, and a postponed review then fails the report job. The review's
jobs pass while a review is postponed, so a protection that requires them instead of the status does not gate.

- *As built (#474):* the limit is recognised by Copilot's exit code together with its message (`usage limit`,
  `rate limit` or `too many requests`); any other failure still fails the review. The review job's outputs come from
  the job that reads the untrusted diff, so they can only hold the gate closed: a postponement leaves the status
  pending. A verdict is kept only from the workflow's own reviews (`github-actions[bot]`), since anyone can post a
  review, and only for the same reviewing family, so the family rule holds for every head. A Dependabot update that is exempt from review gets no status,
  because Dependabot's runs get a read-only token; #480 has the scheduled retry set it.
