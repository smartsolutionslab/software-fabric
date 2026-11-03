---
name: verify
description: Phase 5, between the engineer and the reviewer: observe the change working end to end, not only its tests green, and write what was observed into the pull request.
---

# Verify the change

Argument: what to verify. Without one, verify the change on the current branch (`git diff origin/main...HEAD --stat`).

The tests answered whether the code does what the tests say. This step answers whether the system does what the issue says: **has anyone watched it behave?** A test that only reads source files, a re-reading of the spec, or "the unit tests cover it" does not answer that.

1. **Pick the smallest thing that observes the behaviour itself**, with the commands AGENTS.md gives:
   - Code behind an endpoint, a consumer or the database: start the application (`dotnet run --project src/AppHost`) and call it as a user or another service would, or run the integration tests against the running stack.
   - A screen: open it in the running application, or run its end-to-end tests with the application up.
   - A build, CI or generator change: run the command a developer would run, on a clean checkout, and read its output.
   - Prefer the running application; it is the strongest evidence.
2. **Write the note** under `## How it was verified` in the pull request description. It says:
   - **what was observed**: the actual behaviour, in a sentence;
   - **how**: the exact command, request or click path;
   - **the output**: quoted in a code block, not described;
   - **what was not covered**: the honest gap. Every verification has one.
3. **If the behaviour does not hold**, say so in the note and hand the work back to the engineer. Never record a pass nobody watched: it turns an unknown into a false record.

This step changes no code and no tests. What it finds goes back to the engineer; the reviewer then reads the diff and the note.