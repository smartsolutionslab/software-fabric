## Test discipline: test after

- Tests may follow the code, but no change is done without them: every new behaviour gets a test that checks its result, not only one that runs it.
- CI's coverage gate fails below the thresholds in `tests/coverage.props`. Run `bash .github/scripts/test.sh` after a Release build before pushing; add tests until it passes.
- Never lower a threshold, exclude code from coverage or delete a test to get to green.
