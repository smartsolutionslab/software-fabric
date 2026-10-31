#!/usr/bin/env bash
# Every commit on an agent's branch says who made it (board protocol, docs/adr).
# Usage: check-trailers.sh <branch> <base sha> <head sha>
set -euo pipefail

branch="$1"
if [[ "$branch" != agent/* ]]; then
  echo "Not an agent branch; nothing to check."
  exit 0
fi

missing="$(git log --format='%h%x09%(trailers:key=Agent-Executor,valueonly,separator=%x2C)%x09%s' "$2..$3" | awk -F '\t' '$2 == "" { print "  " $1 " " $3 }')"
if [ -n "$missing" ]; then
  echo "These commits on $branch have no Agent-Executor trailer (e.g. 'Agent-Executor: claude-cloud/lane-backend'):"
  echo "$missing"
  exit 1
fi

echo "Every commit names its executor."

