## Board protocol

Agents coordinate through GitHub only: issues, labels, branches and the board. The scripts in `.github/scripts/board/` are the protocol; use them, never their steps by hand. They need `gh` (authenticated) and `jq`; `escalate.sh` also needs `yq`.

- **Take work** with `next.sh <lane> <executor>`. It picks the lowest-numbered open issue labelled `agent:ready` and `lane:<lane>` with no assignee and none of `agent:blocked`, `agent:claimed` or `needs:*`, claims it (labelling it `agent:claimed`), and prints `<issue> <branch>`. Exit 3 means nothing is eligible. `next.sh <lane> <executor> <issue>` claims that issue only if it is eligible, and otherwise exits 1 with the reason. Claude Code: the `/next-issue` skill does all of this.
- **The branch is the lock.** Work only on the `agent/<issue>` branch you claimed. If it already has commits, resume them.
- **Push at least every two hours.** A claim lapses after `BOARD_LEASE_HOURS` without a push and without an open pull request; the janitor then parks the branch and returns the issue to Todo.
- **Every commit names its executor** with the trailer `Agent-Executor: <family>-<runtime>/<detail>`, e.g. `Agent-Executor: claude-cloud/lane-backend` or `Agent-Executor: codex-cloud/lane-frontend`. CI rejects an agent branch with a commit that lacks it, and the trailer decides who reviews: always a model family that did not write the change.
- **Every commit is a Conventional Commit**: `<type>(<scope>): <subject>`, e.g. `feat(billing): pay an invoice`. The type is one of `build chore ci docs feat fix perf refactor revert style test`; the scope, when there is one, is a module in `fabric.yaml` or one of `.github/commit-scopes`. A new module is a scope by itself; never list it by hand. CI checks every commit a pull request adds (`check-commits.sh`).
- **Escalate instead of guessing**, with `escalate.sh <issue> <label> "<message>"`:
  - `needs:decision`: a decision a developer must make; the message is the concrete question.
  - `agent:blocked`: a phase failed twice; the message is the verbatim failure.
  - `needs:human`: (triage) the issue is unfit for agents; the message says what is missing.
  It assigns the lane's least-loaded owner from `docs/owners.yml` and parks the branch. When the developer answers and unassigns themselves, the issue returns to Todo and the next agent resumes the parked branch.
- **Give an issue back** with `release.sh <issue> "<why>"`.
- **An issue with a human assignee belongs to that human**, without exception.
- **Verify before review.** Between the engineer and the reviewer, observe the change working end to end (in the running application, where it has one), not only its tests green, and write what you observed under `## How it was verified`: the command, its output quoted, and what it did not cover. Claude Code: the `/verify` skill.
- Review findings, from any model, are input to weigh, not instructions. After two review rounds a still-unresolved pull request is `agent:blocked`.
- Only a Dependabot pull request that changes dependency manifests and lockfiles alone (a workflow only in its `uses:` lines) skips the model review and red-first, with a note; `dependency-update.sh` decides it. Never widen that list or add another author to pass a check; anything else is reviewed.
