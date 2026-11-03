#!/usr/bin/env bash
# Red first (docs/adr): a pull request that changes source code quotes its tests failing before the change, or, for a
# behaviour-preserving change, its covering tests passing before it.
# Usage: check-red-first.sh <pull request body file> <file listing the changed paths>
set -euo pipefail

body="$1"
changed="$2"

if ! grep -qE '^(src|web)/' "$changed"; then
  echo "No source changed; nothing to check."
  exit 0
fi

# New behaviour quotes its tests failing; a refactor quotes its covering tests passing before the change instead.
quoted() {
  awk -v heading="$1" 'index($0, heading) == 1 { found = 1; next } /^## / { if (found) exit } found' "$body" | grep '^```' > /dev/null
}

if quoted '## Tests observed failing'; then
  echo "Red first: the failing test output is quoted."
  exit 0
fi

if quoted '## Tests observed passing before the change'; then
  echo "Red first: a behaviour-preserving change quotes its covering tests passing before the change."
  exit 0
fi

echo "This pull request changes source code, but its description quotes no test output in a code block under '## Tests observed failing' (new behaviour) or '## Tests observed passing before the change' (a refactor) (red first, see docs/adr)."
exit 1
