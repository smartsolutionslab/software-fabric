#!/usr/bin/env bash
# Picks the model family that reviews a pull request: never the family that wrote it (board protocol, docs/adr).
# Usage: reviewer.sh <base sha> <head sha>
# Reads the Agent-Executor trailers of the commits in base..head; prints claude or codex.
source "$(dirname "${BASH_SOURCE[0]}")/lib.sh"

families="$(git log --format='%(trailers:key=Agent-Executor,valueonly)' "$1..$2" | sed -E '/^$/d; s#[-/].*##' | sort -u)"

case "$families" in
  "") echo "${BOARD_DEFAULT_REVIEWER}" ;;
  claude) echo codex ;;
  codex) echo claude ;;
  *)
    echo "Commits by more than one model family ($(tr '\n' ' ' <<< "$families")): no family is independent of them all." >&2
    exit 1
    ;;
esac

