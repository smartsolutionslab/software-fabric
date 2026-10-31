#!/usr/bin/env bash
# Red first (docs/adr): a pull request that changes source code quotes its tests failing before the change.
# Usage: check-red-first.sh <pull request body file> <file listing the changed paths>
set -euo pipefail

body="$1"
changed="$2"

if ! grep -qE '^(src|web)/' "$changed"; then
  echo "No source changed; nothing to check."
  exit 0
fi

section="$(awk '/^## Tests observed failing/ { found = 1; next } /^## / { if (found) exit } found' "$body")"
if ! printf '%s\n' "$section" | grep -q '^```'; then
  echo "This pull request changes source code, but its description has no '## Tests observed failing' section quoting the test output in a code block (red first, see docs/adr)."
  exit 1
fi

echo "Red first: the failing test output is quoted."
