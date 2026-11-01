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
comment.

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
- *As built (#96):* the reviewer runs as GitHub Copilot CLI, headless, with
  the workflow's own token (`copilot-requests: write`) instead of each
  vendor's action and API key. The family rule is unchanged; the family
  picks the model, pinned in `cross-model-review.yml` with the CLI's version.
  Since the reviewer holds no secret, the reason above for not using
  `pull_request_target` is now the token's write access to pull requests.
