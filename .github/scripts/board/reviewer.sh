#!/usr/bin/env bash
# Picks the model family that reviews a pull request: never the family that wrote it (board protocol, docs/adr).
# Usage: reviewer.sh <base sha> <head sha>
# Reads the Agent-Executor trailers of the commits in base..head, and Co-Authored-By trailers naming Claude or Codex
# (a developer working with an assistant); prints claude or codex.
source "$(dirname "${BASH_SOURCE[0]}")/lib.sh"

families="$(
  {
    git log --format='%(trailers:key=Agent-Executor,valueonly)' "$1..$2" | sed -E '/^$/d; s#[-/].*##'
    git log --format='%(trailers:key=Co-Authored-By,valueonly)' "$1..$2" | sed -nE 's/^.*\b(claude|codex)\b.*$/\1/Ip' | tr '[:upper:]' '[:lower:]'
  } | sort -u
)"

case "$families" in
  "") reviewer="${BOARD_DEFAULT_REVIEWER:-}" ;;
  claude) reviewer=codex ;;
  codex) reviewer=claude ;;
  *)
    echo "Commits by more than one model family ($(tr '\n' ' ' <<< "$families")): no family is independent of them all." >&2
    exit 1
    ;;
esac

# Anything else would match neither review job, and the workflow would pass without a review.
case "$reviewer" in
  claude | codex) echo "$reviewer" ;;
  *)
    echo "The reviewer is '$reviewer' (BOARD_DEFAULT_REVIEWER); it must be claude or codex." >&2
    exit 1
    ;;
esac

