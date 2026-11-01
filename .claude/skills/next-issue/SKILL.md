---
name: next-issue
description: Claim the next eligible issue of a lane from the board and deliver it end to end through the phases in AGENTS.md; escalate instead of guessing.
---

# Deliver the next issue

Arguments: the lane (one of the lanes in `docs/owners.yml`) and this run's executor, `<family>-<runtime>/<detail>`, e.g. `claude-cloud/lane-backend` or `claude-local/<host>`.

1. **Claim.** Run `bash .github/scripts/board/next.sh <lane> <executor>`.
   - Exit 3: nothing is eligible in the lane. Stop and say so.
   - Exit 1: a claim failed for another reason, printed above (permissions, network, a refused push). Nothing was claimed. Stop and report that output verbatim; do not do the script's steps by hand.
   - Otherwise it prints `<issue> <branch>`. Fetch and check out that branch. If it already has commits, a previous agent left them: read them and resume, do not start over.
2. **Work the phases** in AGENTS.md, each in its own subagent from `.claude/agents/`, handing each the artifact of the one before:
   architect → test-writer (returns the failing output verbatim) → the lane's engineer (may not edit those tests) → the lane's reviewer.
   - The architect's spec, plan and tasks go into the pull request description under `## Spec and plan`.
   - **Without subagents** (your runtime cannot start them): work the same phases yourself, in that order, and keep them apart. Commit the tests alone first, run them and keep their failing output; then implement in later commits without touching those test files; then review your own diff against the lane reviewer's brief before opening the pull request.
3. **Commit and push often.** Every push renews the claim; after the lease (`BOARD_LEASE_HOURS` in `.github/board.env`, 2 h unless changed) without one, the janitor returns the issue to Todo.
   Every commit ends with the trailer `Agent-Executor: <executor>`; CI rejects an agent branch with a commit that lacks it.
4. **Open the pull request** with the template, quoting the test-writer's failing output. CI and a reviewer from a different model family run on it.
5. **When you cannot go on**, escalate rather than guess. One call; it assigns a developer from `docs/owners.yml` and parks the branch:
   - a decision only a developer can make: `bash .github/scripts/board/escalate.sh <issue> needs:decision "<the concrete question>"`
   - a phase that failed twice: `bash .github/scripts/board/escalate.sh <issue> agent:blocked "<the verbatim failure>"`
   Then take the next issue: one stuck issue must not end the run.
6. **Giving an issue back** without escalating (it belongs to another lane, it is already done): `bash .github/scripts/board/release.sh <issue> "<why>"`.

Never take an issue with a human assignee, and never work on a branch you did not claim.
