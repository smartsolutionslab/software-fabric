#!/usr/bin/env bash
# The pairwise matrix on every pull request (ADR-0003 amendment, roadmap M3-03): generates every combination of
# build/matrix.json (or the file --matrix names) with build/profile-ci.sh --steps generate, the nightly run's own
# generate step, one after another in this one job. A combination that fails to generate gets an ::error:: naming it
# and its first error, and the run goes on to the next; once every combination has been tried, the script fails,
# listing those that did not generate. Building and testing them is the nightly run's (M3-04).
# Needs a Release build of the CLI, and jq.
# Usage: build/matrix-generate.sh [--matrix <file>]
set -euo pipefail

repository="$(cd "$(dirname "$0")/.." && pwd)"
matrix="$repository/build/matrix.json"
if [[ $# -gt 0 ]]; then
  [[ $# -eq 2 && $1 == --matrix && -f $2 ]] || { echo "Usage: build/matrix-generate.sh [--matrix <file>]" >&2; exit 2; }
  matrix="$2"
fi

# Assigned first: a failing command substitution inside the for list would not stop the script.
names="$(jq --raw-output '.combinations[].name' "$matrix")"
total=0
failed=()
for name in $names; do
  total=$((total + 1))
  if output="$(bash "$repository/build/profile-ci.sh" --combination "$name" --matrix "$matrix" --steps generate 2>&1)"; then
    echo "$name: generated"
  else
    first="$(grep --max-count=1 '^error' <<<"$output" || tail -n 1 <<<"$output")"
    echo "::error title=matrix generation::$name: $first"
    failed+=("$name")
  fi
done

if [[ ${#failed[@]} -gt 0 ]]; then
  listed="$(printf '%s, ' "${failed[@]}")"
  echo "${#failed[@]} of $total combinations failed to generate: ${listed%, }" >&2
  exit 1
fi
echo "every combination generated: $total of $total"
