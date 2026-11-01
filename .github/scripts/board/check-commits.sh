#!/usr/bin/env bash
# Every commit a pull request adds is a Conventional Commit, <type>(<scope>)!: <subject> (board protocol, docs/adr).
# The scopes are the project's: its modules in fabric.yaml and the fixed ones in .github/commit-scopes.
# Merge commits are not checked. Needs git alone, so a project without a frontend needs no Node toolchain for it.
# Usage: check-commits.sh <base sha> <head sha>
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)"
types=(build chore ci docs feat fix perf refactor revert style test)
max_header=120

scopes=()
if [ -f "$root/.github/commit-scopes" ]; then
  while read -r scope; do
    scopes+=("$scope")
  done < <(grep -vE '^[[:space:]]*(#|$)' "$root/.github/commit-scopes")
fi
if [ -f "$root/fabric.yaml" ]; then
  while read -r module; do
    scopes+=("$module")
  done < <(awk '/^modules:/ { inside = 1; next } inside && /^[^ ]/ { inside = 0 } inside && /^  [^ #][^:]*:/ { sub(/^  /, ""); sub(/:.*/, ""); print }' "$root/fabric.yaml")
fi

one_of() {
  local word="$1"
  shift
  local candidate
  for candidate in "$@"; do
    if [ "$candidate" = "$word" ]; then
      return 0
    fi
  done
  return 1
}

# Why a header is not a Conventional Commit, or nothing.
problem() {
  local header="$1"
  local pattern='^([a-z]+)(\(([^)]*)\))?!?: (.*[^[:space:]].*)$'
  if [[ ! "$header" =~ $pattern ]]; then
    echo "not <type>(<scope>): <subject>"
  elif ! one_of "${BASH_REMATCH[1]}" "${types[@]}"; then
    echo "type '${BASH_REMATCH[1]}' is not one of: ${types[*]}"
  elif [ -n "${BASH_REMATCH[2]}" ] && ! one_of "${BASH_REMATCH[3]}" "${scopes[@]}"; then
    echo "scope '${BASH_REMATCH[3]}' is not one of: ${scopes[*]}"
  elif [ "${#header}" -gt "$max_header" ]; then
    echo "longer than $max_header characters"
  fi
}

commits="$(git log --no-merges --format='%h%x09%s' "$1..$2")"
failures=""
while IFS=$'\t' read -r commit header; do
  if [ -z "$commit" ]; then
    continue
  fi
  reason="$(problem "$header")"
  if [ -n "$reason" ]; then
    failures+="  $commit $header: $reason"$'\n'
  fi
done <<< "$commits"

if [ -n "$failures" ]; then
  echo "These commits are not Conventional Commits (<type>(<scope>)!: <subject>, e.g. 'feat(board): claim by label'):"
  printf '%s' "$failures"
  exit 1
fi

echo "Every commit is a Conventional Commit."

