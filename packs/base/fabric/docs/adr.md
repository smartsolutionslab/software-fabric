# Board protocol

**Status:** Accepted (generated)

## Decision

Agents coordinate through GitHub, never with each other, so any runtime that can run `gh` takes part: Claude Code in the cloud or locally, Codex, GitHub Actions. The protocol is the scripts in `.github/scripts/board/`, one per step: `next.sh` claims, `release.sh` gives back, `escalate.sh` hands to a developer, `janitor.sh` (hourly) and `return-to-todo.sh` (on unassignment) return issues to Todo.

- **Eligibility** is labels: `agent:ready`, `lane:<lane>`, not `agent:blocked`, no assignee. A human assignee always wins.
- **The branch is the lock.** Claiming creates `agent/<issue>-<slug>` through the API, which fails if it exists; resuming renames `parked/<issue>-…` back, which only one agent can do. A released, escalated or lapsed claim parks its branch instead of deleting it, so the work survives for the next agent.
- **Lease.** A claim lapses when the branch has had no push and no claim comment within `BOARD_LEASE_HOURS` (2 h by default) and has no open pull request.
- **Escalation** labels the issue (`needs:human`, `needs:decision`, `agent:blocked`), assigns the owner of its lane with the fewest open assignments below the WIP limit (`docs/owners.yml`), and says why in a comment.
- **Board fields.** When `.github/board.env` names a GitHub Project, the scripts keep its `Status` (Todo, Claimed, In Progress, In Review, Blocked, Done) and `Executor` fields current; without one, labels and comments carry the protocol alone.
- **Review across model families.** Every agent commit carries `Agent-Executor: <family>-<runtime>/<detail>`; the review workflow has the other family review the pull request. A pull request with commits from both families has no independent reviewer and fails that check.

## Consequences

- No dispatcher to keep alive: the state is on GitHub. A crashed agent costs one lease, not a stuck issue.
- Workflows changing the board need a token with the `project` scope (`BOARD_TOKEN`); the default token can change labels, branches and comments only.
- The review workflow needs the reviewing family's API key as a secret (`ANTHROPIC_API_KEY`, `OPENAI_API_KEY`).
